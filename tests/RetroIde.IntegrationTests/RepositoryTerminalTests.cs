using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application.Git;
using RetroIde.Application.Terminals;
using RetroIde.Testing;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed class RepositoryTerminalTests
{
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [Fact]
    public async Task Native_shell_starts_in_the_captured_worktree_and_supports_input_resize_and_exit()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(false);
        var linked = Assert.IsType<GitSelection>(await NativeGit.DiscoverAsync(sample.Linked, "", token).ConfigureAwait(false));
        var shell = OperatingSystem.IsWindows()
            ? new RepositoryTerminal.Shell(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/D", "/Q"])
            : new RepositoryTerminal.Shell("/bin/sh", ["-i"]);

        await using (var terminal = await RepositoryTerminal.StartAsync(linked.Repository, 80, 24, NullLogger.Instance, token, shell)
            .ConfigureAwait(false))
        {
            Assert.True(terminal.IsRunning);
            Assert.True(terminal.ProcessId > 0);
            await SendAsync(terminal, "git branch --show-current", token).ConfigureAwait(false);
            var branch = await ReadUntilAsync(terminal, 0, "fixture-linked", token).ConfigureAwait(false);
            Assert.Contains("fixture-linked", branch.Text, StringComparison.Ordinal);

            await SendAsync(terminal, "git rev-parse --show-toplevel", token).ConfigureAwait(false);
            var expected = sample.Linked.Replace(Path.DirectorySeparatorChar, '/');
            var directory = await ReadUntilAsync(terminal, branch.Offset, expected, token).ConfigureAwait(false);
            Assert.Contains(expected, directory.Text, StringComparison.OrdinalIgnoreCase);

            await terminal.ResizeAsync(104, 42, token).ConfigureAwait(false);
            var dimensionCommand = OperatingSystem.IsWindows() ? "mode con" : "stty size";
            await SendAsync(terminal, dimensionCommand, token).ConfigureAwait(false);
            var dimensions = await ReadUntilAsync(terminal, directory.Offset, OperatingSystem.IsWindows() ? "104" : "42 104", token)
                .ConfigureAwait(false);
            Assert.Contains("104", dimensions.Text, StringComparison.Ordinal);
            Assert.Contains("42", dimensions.Text, StringComparison.Ordinal);

            await SendAsync(terminal, "git update-ref refs/heads/retro-terminal-proof HEAD", token).ConfigureAwait(false);
            await SendAsync(terminal, "exit 23", token).ConfigureAwait(false);
            var exit = await terminal.WaitForExitAsync(token).ConfigureAwait(false);
            Assert.Equal(23, exit.Code);
            Assert.Equal(0, exit.Signal);
            Assert.False(terminal.IsRunning);
        }

        var recorded = (await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/heads/retro-terminal-proof").ConfigureAwait(false)).Trim();
        var head = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        Assert.Equal(head, recorded);
        var idle = await RepositoryTerminal.StartAsync(linked.Repository, 80, 24, NullLogger.Instance, token, shell).ConfigureAwait(false);
        await idle.DisposeAsync().AsTask().WaitAsync(token).ConfigureAwait(false);
        await idle.DisposeAsync().ConfigureAwait(false);
        Assert.False(idle.IsRunning);
        Assert.NotNull(idle.ExitResult);
    }

    [Fact]
    public void Output_retention_keeps_absolute_offsets_and_reports_eviction()
    {
        var transcript = new TerminalTranscript(8);
        transcript.Append("hello"u8);
        var first = transcript.Read(0, 3);
        Assert.Equal("hel"u8.ToArray(), first.Bytes);
        Assert.Equal(3, first.NextOffset);
        transcript.Append(" world!"u8);
        var retained = transcript.Read(first.NextOffset, 32);
        Assert.True(retained.Truncated);
        Assert.Equal(4, retained.OldestOffset);
        Assert.Equal("o world!"u8.ToArray(), retained.Bytes);
        Assert.Equal(12, retained.NextOffset);

        transcript.Append("0123456789"u8);
        var overwritten = transcript.Read(0, 32);
        Assert.Equal(14, overwritten.StartOffset);
        Assert.Equal(22, overwritten.NextOffset);
        Assert.Equal("23456789"u8.ToArray(), overwritten.Bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => transcript.Read(23, 1));
    }

    private static Task SendAsync(RepositoryTerminal terminal, string command, CancellationToken token) =>
        terminal.WriteAsync(Encoding.UTF8.GetBytes(command + "\r"), token);

    private static async Task<(string Text, long Offset)> ReadUntilAsync(RepositoryTerminal terminal, long offset, string expected,
        CancellationToken token)
    {
        using var output = new MemoryStream();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var part = terminal.Read(offset, 16 * 1024);
            Assert.False(part.Truncated);
            output.Write(part.Bytes);
            offset = part.NextOffset;
            var text = Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            if (text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                return (text, offset);
            await Task.Delay(15, token).ConfigureAwait(false);
        }
    }
}
