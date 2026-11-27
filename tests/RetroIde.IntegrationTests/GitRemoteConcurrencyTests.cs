using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed class GitRemoteConcurrencyTests
{
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaFact]
    public async Task Linked_worktrees_cannot_both_write_the_same_reviewed_remote_revision()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var primary = new WorkbenchSession(Guid.NewGuid());
        await using var linked = new WorkbenchSession(Guid.NewGuid());
        await using var direct = await Connection.ConnectAsync(primary, token).ConfigureAwait(true);
        await using var other = await Connection.ConnectAsync(linked, token).ConfigureAwait(true);
        await using var relay = await other.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "remote-revision-stdio.json"))
            .ConfigureAwait(true);
        await SelectAsync(direct, fixture.Primary).ConfigureAwait(true);
        await SelectAsync(other, fixture.Linked).ConfigureAwait(true);

        var left = await ReviewAsync(direct, fixture.Primary).ConfigureAwait(true);
        var right = await ReviewAsync(other, fixture.Linked, relay).ConfigureAwait(true);
        Assert.Equal(left, right, StringComparer.Ordinal);

        var first = WriteAsync(direct, fixture.Primary, left, "https://first.invalid/review");
        var second = WriteAsync(other, fixture.Linked, right, "https://second.invalid/review", relay);
        var completed = await Task.WhenAll(first, second).WaitAsync(token).ConfigureAwait(true);
        var accepted = Assert.Single(completed, result => string.Equals(result.Code, "ok", StringComparison.Ordinal));
        Assert.Single(completed, result => string.Equals(result.Code, "git_remote_changed", StringComparison.Ordinal));
        var actual = await fixture.GitAsync(fixture.Primary, token, "config", "--get-all", "remote.origin.url").ConfigureAwait(true);
        Assert.Equal(accepted.Url, actual.Trim(), StringComparer.Ordinal);
        var linkedValue = await fixture.GitAsync(fixture.Linked, token, "config", "--get-all", "remote.origin.url").ConfigureAwait(true);
        Assert.Equal(accepted.Url, linkedValue.Trim(), StringComparer.Ordinal);
    }

    private static async Task SelectAsync(Connection connection, string path)
    {
        var root = await connection.AddRootAsync(path).ConfigureAwait(false);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root,
            ["relativePath"] = "",
        }).ConfigureAwait(false);
    }

    private static async Task<string> ReviewAsync(Connection connection, string path, McpClient? transport = null)
    {
        var result = await connection.CallAsync("git_list_remotes", new(StringComparer.Ordinal) { ["worktreePath"] = path }, transport)
            .ConfigureAwait(false);
        Assert.Equal("ok", Code(result), StringComparer.Ordinal);
        return Assert.IsType<string>(result.GetProperty("data").GetProperty("revision").GetString());
    }

    private static async Task<(string? Code, string Url)> WriteAsync(Connection connection, string path, string revision,
        string url, McpClient? transport = null)
    {
        var current = await connection.CallAsync("workbench_get_context", transport: transport).ConfigureAwait(false);
        var result = await connection.CallAsync("git_set_remote_url", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = path,
            ["name"] = "origin",
            ["revision"] = revision,
            ["value"] = url,
            ["context"] = current.GetProperty("context"),
        }, transport).ConfigureAwait(false);
        return (Code(result), url);
    }

    private static string? Code(JsonElement result) => result.GetProperty("code").GetString();
}
