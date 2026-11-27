using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class GitRemoteConcurrencyTests
{
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaFact]
    public async Task Saving_an_included_file_in_another_checkout_waits_for_the_reviewed_transfer()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = LocalEndpoint(listener);
        var peer = await ConfigureCrossCheckoutIncludeAsync(fixture, endpoint, token).ConfigureAwait(true);
        var primary = GitPath(fixture.Primary);
        await fixture.GitAsync(primary, token, "config", "--local", "http.proxy", "").ConfigureAwait(true);

        await using var transferSession = new WorkbenchSession(Guid.NewGuid());
        await using var editorSession = new WorkbenchSession(Guid.NewGuid());
        await using var transfer = await Connection.ConnectAsync(transferSession, token).ConfigureAwait(true);
        await using var editor = await Connection.ConnectAsync(editorSession, token).ConfigureAwait(true);
        await using var relay = await editor.ConnectStdioAsync(Path.Combine(peer, ".git", "config-save-relay.json"))
            .ConfigureAwait(true);
        await SelectAsync(transfer, primary).ConfigureAwait(true);
        var root = await editor.AddRootAsync(peer).ConfigureAwait(true);
        var opened = await editor.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = IncludedRemoteFile }).ConfigureAwait(true);
        var original = await File.ReadAllTextAsync(Path.Combine(peer, IncludedRemoteFile), token).ConfigureAwait(true);
        var offset = original.IndexOf(endpoint, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var edit = await editor.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = opened.GetProperty("data"), ["offset"] = offset, ["length"] = endpoint.Length, ["text"] = ReplacementEndpoint,
        }).ConfigureAwait(true);

        var operation = await StartReviewedFetchAsync(transfer, primary).ConfigureAwait(true);
        using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);
        var save = editor.ChangeAsync("editor_save_document", new(StringComparer.Ordinal) { ["document"] = edit.GetProperty("data") }, relay);
        var savedEarly = ReferenceEquals(await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true), save);
        var observed = await ReadEffectiveEndpointAsync(fixture, primary, token).ConfigureAwait(true);
        await RejectRequestAsync(request, token).ConfigureAwait(true);
        Assert.Equal("ok", Code(await save.WaitAsync(token).ConfigureAwait(true)), StringComparer.Ordinal);
        await VerifyTransferCompletedAsync(transfer, operation, "git_fetch_failed", token).ConfigureAwait(true);

        Assert.False(savedEarly, $"The editor save completed during the reviewed transfer. Native Git read {observed}.");
        Assert.Equal(endpoint, observed, StringComparer.Ordinal);
        Assert.Equal(ReplacementEndpoint, await ReadEffectiveEndpointAsync(fixture, primary, token).ConfigureAwait(true), StringComparer.Ordinal);
    }

    /// <remarks>TR-RETRO-GIT-013#ac-git-machine-state; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaTheory]
    [InlineData("git_fetch")]
    [InlineData("git_pull")]
    [InlineData("git_push")]
    public async Task A_named_transfer_rejects_replaced_checkout_metadata_after_waiting(string tool)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = LocalEndpoint(listener);
        await PointOriginAsync(fixture, endpoint, token).ConfigureAwait(true);
        var primary = GitPath(fixture.Primary);
        var linked = GitPath(fixture.Linked);
        var replacement = await PrepareReplacementMetadataAsync(fixture, linked, endpoint, token).ConfigureAwait(true);
        var original = GitPath((await fixture.GitAsync(linked, token, "rev-parse", "--absolute-git-dir").ConfigureAwait(true)).Trim());

        await using var first = new WorkbenchSession(Guid.NewGuid());
        await using var second = new WorkbenchSession(Guid.NewGuid());
        await using var blocker = await Connection.ConnectAsync(first, token).ConfigureAwait(true);
        await using var transfer = await Connection.ConnectAsync(second, token).ConfigureAwait(true);
        await SelectAsync(blocker, primary).ConfigureAwait(true);
        await SelectAsync(transfer, linked).ConfigureAwait(true);
        var args = await ReviewedNamedTransferAsync(transfer, fixture, linked, tool, token).ConfigureAwait(true);
        var active = await StartReviewedFetchAsync(blocker, primary).ConfigureAwait(true);
        using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);

        var queued = transfer.CallAsync(tool, args);
        Assert.NotSame(queued, await Task.WhenAny(queued, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true));
        await ReplaceGitPointerAsync(linked, replacement, token).ConfigureAwait(true);
        var current = GitPath((await fixture.GitAsync(linked, token, "rev-parse", "--absolute-git-dir").ConfigureAwait(true)).Trim());
        Assert.Equal(replacement, current, StringComparer.Ordinal);
        Assert.True(Directory.Exists(original), "The previous Git metadata must remain usable with explicit native arguments.");
        await RejectRequestAsync(request, token).ConfigureAwait(true);
        await VerifyTransferCompletedAsync(blocker, active, "git_fetch_failed", token).ConfigureAwait(true);

        using var observe = CancellationTokenSource.CreateLinkedTokenSource(token);
        var unexpected = listener.AcceptTcpClientAsync(observe.Token).AsTask();
        var opened = ReferenceEquals(unexpected, await Task.WhenAny(unexpected, queued).WaitAsync(token).ConfigureAwait(true));
        if (opened)
        {
            using var requestFromStaleCheckout = await unexpected.ConfigureAwait(true);
            await RejectRequestAsync(requestFromStaleCheckout, token).ConfigureAwait(true);
        }
        await observe.CancelAsync().ConfigureAwait(true);
        var result = Code(await queued.WaitAsync(token).ConfigureAwait(true));
        Assert.False(opened, $"{tool} opened a native connection using the stale metadata. It returned {result}.");
        Assert.Equal("git_repository_unavailable", result, StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, object?>> ReviewedNamedTransferAsync(Connection connection, GitWorktreeFixture fixture,
        string path, string tool, CancellationToken token)
    {
        var current = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["worktreePath"] = path, ["name"] = "origin", ["revision"] = await ReviewAsync(connection, path).ConfigureAwait(false),
            ["context"] = current.GetProperty("context"),
        };
        if (!string.Equals(tool, "git_fetch", StringComparison.Ordinal))
        {
            args["branchName"] = (await fixture.GitAsync(path, token, "branch", "--show-current").ConfigureAwait(false)).Trim();
            args["expectedHead"] = (await fixture.GitAsync(path, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
            args["remoteBranch"] = "main";
        }
        return args;
    }
}
