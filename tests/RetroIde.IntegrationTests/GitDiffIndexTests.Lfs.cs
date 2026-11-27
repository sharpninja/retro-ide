using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Git_lfs_fetch_and_checkout_respect_native_filters_and_editor_scope()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        var version = await sample.RunAsync(sample.Primary, token, "lfs", "version").ConfigureAwait(true);
        Assert.SkipWhen(version.ExitCode != 0, "This installed Git LFS integration fixture requires git-lfs on PATH.");
        const string Relative = "Café [sample].rom";
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var readerPath = Path.Combine(sample.Peer, Relative);
        await sample.GitAsync(sample.Primary, token, "lfs", "install", "--local", "--skip-repo").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "lfs", "track", "*.rom").ConfigureAwait(true);
        await File.WriteAllBytesAsync(Path.Combine(sample.Primary, Relative), bytes, token).ConfigureAwait(true);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        var publisher = await connection.AddRootAsync(sample.Primary).ConfigureAwait(true);
        await SelectAsync(publisher).ConfigureAwait(true);
        await StageAsync(".gitattributes").ConfigureAwait(true);
        await StageAsync(Relative).ConfigureAwait(true);
        var objectId = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Contains("oid sha256:" + objectId, await sample.GitAsync(sample.Primary, token, "show", ":" + Relative).ConfigureAwait(true),
            StringComparison.Ordinal);
        await sample.GitAsync(sample.Primary, token, "commit", "-m", "Store fixture media").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "lfs", "push", "origin", "main").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "push", "origin", "main").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "pull", "--ff-only", "origin", "main").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "lfs", "install", "--local", "--skip-repo").ConfigureAwait(true);
        var pointer = await File.ReadAllBytesAsync(readerPath, token).ConfigureAwait(true);
        Assert.False(pointer.AsSpan().SequenceEqual(bytes));

        await using var relay = await connection.ConnectStdioAsync(Path.Combine(sample.Peer, ".git", "lfs-stdio.json")).ConfigureAwait(true);
        var root = await connection.AddRootAsync(sample.Peer).ConfigureAwait(true);
        await SelectAsync(root).ConfigureAwait(true);
        var initial = await ReadAsync().ConfigureAwait(true);
        var listed = Assert.Single(initial.GetProperty("files").EnumerateArray());
        Assert.True(initial.GetProperty("isInstalled").GetBoolean());
        Assert.True(initial.GetProperty("hasFilter").GetBoolean());
        Assert.Equal(Relative, listed.GetProperty("path").GetString());
        Assert.Equal(objectId, listed.GetProperty("objectId").GetString());
        Assert.False(listed.GetProperty("isDownloaded").GetBoolean());
        Assert.False(listed.GetProperty("isMaterialized").GetBoolean());

        Assert.Equal("git_lfs_remote_unavailable", Code(await ChangeAsync("git_lfs_fetch", initial, "missing").ConfigureAwait(true)));
        var fetched = (await ChangeAsync("git_lfs_fetch", initial, "origin", relay).ConfigureAwait(true)).GetProperty("data");
        Assert.True(Assert.Single(fetched.GetProperty("files").EnumerateArray()).GetProperty("isDownloaded").GetBoolean());
        Assert.Equal<byte>(pointer, await File.ReadAllBytesAsync(readerPath, token).ConfigureAwait(true));
        Assert.Equal("git_lfs_changed", Code(await ChangeAsync("git_lfs_checkout", initial).ConfigureAwait(true)));

        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = Relative }).ConfigureAwait(true);
        var editor = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var originalText = editor.Buffer.Text;
        await EditAsync("unsaved local text").ConfigureAwait(true);
        Assert.Equal("git_lfs_editors_busy", Code(await ChangeAsync("git_lfs_checkout", fetched).ConfigureAwait(true)));
        Assert.Equal("unsaved local text", editor.Buffer.Text);
        await EditAsync(originalText).ConfigureAwait(true);

        var result = await ChangeAsync("git_lfs_checkout", fetched, client: relay).ConfigureAwait(true);
        Assert.Equal("ok", Code(result));
        Assert.True(Assert.Single(result.GetProperty("data").GetProperty("files").EnumerateArray()).GetProperty("isMaterialized").GetBoolean());
        Assert.Equal<byte>(bytes, await File.ReadAllBytesAsync(readerPath, token).ConfigureAwait(true));
        await File.WriteAllBytesAsync(readerPath, pointer, token).ConfigureAwait(true);
        var pull = await ChangeAsync("git_lfs_pull", await ReadAsync().ConfigureAwait(true), "origin").ConfigureAwait(true);
        Assert.Equal("ok", Code(pull));
        Assert.Equal<byte>(bytes, await File.ReadAllBytesAsync(readerPath, token).ConfigureAwait(true));

        async Task SelectAsync(Guid rootId) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
            { ["rootId"] = rootId, ["relativePath"] = "" }).ConfigureAwait(false);

        async Task StageAsync(string path) => await connection.ChangeAsync("git_stage_file", new(StringComparer.Ordinal)
            { ["worktreePath"] = sample.Primary, ["path"] = path }).ConfigureAwait(false);

        async Task<JsonElement> ReadAsync()
        {
            var reply = await connection.CallAsync("git_lfs_status", new(StringComparer.Ordinal)
                { ["worktreePath"] = sample.Peer }, relay).ConfigureAwait(false);
            Assert.Equal("ok", Code(reply));
            return reply.GetProperty("data");
        }

        async Task<JsonElement> ChangeAsync(string tool, JsonElement reviewed, string? remote = null, McpClient? client = null)
        {
            var context = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            var args = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Peer, ["expectedRevision"] = reviewed.GetProperty("revision").GetString(), ["context"] = context,
            };
            if (remote is not null)
                args["remote"] = remote;
            return await connection.CallAsync(tool, args, client).ConfigureAwait(false);
        }

        async Task EditAsync(string text) => await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = editor.Capture(), ["offset"] = 0, ["length"] = editor.Buffer.TextLength, ["text"] = text,
        }).ConfigureAwait(false);
    }
}
