using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Verifies remote push leases and three pull strategies against a real local bare repository.</summary>
    /// <remarks>FR-RETRO-013; TR-RETRO-GIT-013; TEST-RETRO-013.</remarks>
    [AvaloniaFact]
    public async Task Workbench_pushes_with_leases_and_pulls_reviewed_Git_history()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var checkout = sample.Primary;
        var baseTip = await HeadAsync(checkout);
        var sibling = await HeadAsync(sample.Linked);
        await sample.GitAsync(checkout, token, "config", "--local", "user.name", "Retro Pull Test");
        await sample.GitAsync(checkout, token, "config", "--local", "user.email", "pull@example.invalid");
        await sample.GitAsync(checkout, token, "config", "--local", "commit.gpgsign", "false");
        await sample.GitAsync(checkout, token, "config", "--local", "core.hooksPath",
            Directory.CreateDirectory(Path.Combine(checkout, ".git", "pull-fixture-hooks")).FullName);

        await sample.GitAsync(checkout, token, "commit", "--allow-empty", "-m", "Local push candidate");
        var local = await HeadAsync(checkout);
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Remote advanced");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var advanced = await RemoteTipAsync();

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var bridge = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "sync-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });
        var remotes = await connection.CallAsync("git_list_remotes", new(StringComparer.Ordinal) { ["worktreePath"] = checkout });
        var revision = remotes.GetProperty("data").GetProperty("revision").GetString();

        Assert.Equal("git_push_rejected", Code(await SyncAsync("git_push", local)));
        Assert.Equal("git_push_lease_rejected", Code(await SyncAsync("git_push", local, lease: baseTip, transport: bridge)));
        Assert.Equal(advanced, await RemoteTipAsync());
        Assert.Equal("ok", Code(await SyncAsync("git_push", local, lease: advanced, transport: bridge)));
        Assert.Equal(local, await RemoteTipAsync());

        await sample.GitAsync(sample.Peer, token, "fetch", "origin", "main");
        await sample.GitAsync(sample.Peer, token, "reset", "--hard", "origin/main");
        var path = GitWorktreeFixture.ChangedPath;
        GitWorktreeFixture.Write(sample.Peer, path, "pulled remote text\n");
        await sample.GitAsync(sample.Peer, token, "--literal-pathspecs", "add", "--", path);
        await sample.GitAsync(sample.Peer, token, "commit", "-m", "Remote source change");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var incoming = await RemoteTipAsync();
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = path });
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var initialBuffer = view.Buffer.Text;

        await EditAsync("unsaved text");
        Assert.Equal("git_pull_editors_busy", Code(await SyncAsync("git_pull", local, "fast-forward")));
        Assert.Equal("unsaved text", view.Buffer.Text);
        Assert.Equal(local, await HeadAsync(checkout));
        await EditAsync(initialBuffer);

        Assert.Equal("ok", Code(await SyncAsync("git_pull", local, "fast-forward", transport: bridge)));
        Assert.Equal(incoming, await HeadAsync(checkout));
        Assert.Equal("pulled remote text\n", view.Buffer.Text);
        Assert.False(view.IsDirty);

        await sample.GitAsync(checkout, token, "commit", "--allow-empty", "-m", "Local merge side");
        var mergeLocal = await HeadAsync(checkout);
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Remote merge side");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var mergeRemote = await RemoteTipAsync();
        Assert.Equal("git_pull_diverged", Code(await SyncAsync("git_pull", mergeLocal, "fast-forward")));
        Assert.Equal(mergeLocal, await HeadAsync(checkout));
        Assert.Equal("ok", Code(await SyncAsync("git_pull", mergeLocal, "merge")));
        Assert.Equal(mergeLocal + " " + mergeRemote, (await sample.GitAsync(checkout, token, "show", "-s", "--format=%P", "HEAD")).Trim());

        const string LocalRebasePath = "rebase-local.txt";
        const string IgnoredPath = "build-cache.txt";
        GitWorktreeFixture.Write(checkout, LocalRebasePath, "local rebase content\n");
        await sample.GitAsync(checkout, token, "add", "--", LocalRebasePath);
        await sample.GitAsync(checkout, token, "commit", "-m", "Local rebase side");
        var rebaseLocal = await HeadAsync(checkout);
        await File.AppendAllTextAsync(Path.Combine(checkout, ".git", "info", "exclude"), "\n/build-cache.txt\n", token);
        GitWorktreeFixture.Write(checkout, IgnoredPath, "local ignored content\n");
        GitWorktreeFixture.Write(sample.Peer, IgnoredPath, "remote tracked content\n");
        await sample.GitAsync(sample.Peer, token, "add", "--", IgnoredPath);
        await sample.GitAsync(sample.Peer, token, "commit", "-m", "Remote tracked path");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var rebaseRemote = await RemoteTipAsync();
        Assert.Equal("git_pull_local_changes", Code(await SyncAsync("git_pull", rebaseLocal, "rebase")));
        Assert.Equal(rebaseLocal, await HeadAsync(checkout));
        Assert.Equal("local ignored content\n", await File.ReadAllTextAsync(Path.Combine(checkout, IgnoredPath), token));
        File.Delete(Path.Combine(checkout, IgnoredPath));

        Assert.Equal("ok", Code(await SyncAsync("git_pull", rebaseLocal, "rebase")));
        Assert.Equal(rebaseRemote, (await sample.GitAsync(checkout, token, "merge-base", "HEAD", rebaseRemote)).Trim());
        Assert.NotEqual(rebaseLocal, await HeadAsync(checkout));
        Assert.Equal("local rebase content\n", await File.ReadAllTextAsync(Path.Combine(checkout, LocalRebasePath), token));

        GitWorktreeFixture.Write(checkout, GitWorktreeFixture.ConflictPath, "conflicting local edit\n");
        await sample.GitAsync(checkout, token, "add", "--", GitWorktreeFixture.ConflictPath);
        await sample.GitAsync(checkout, token, "commit", "-m", "Conflict local side");
        var conflictLocal = await HeadAsync(checkout);
        GitWorktreeFixture.Write(sample.Peer, GitWorktreeFixture.ConflictPath, "conflicting remote edit\n");
        await sample.GitAsync(sample.Peer, token, "add", "--", GitWorktreeFixture.ConflictPath);
        await sample.GitAsync(sample.Peer, token, "commit", "-m", "Conflict remote side");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var conflictRemote = await RemoteTipAsync();
        Assert.Equal("git_pull_conflict", Code(await SyncAsync("git_pull", conflictLocal, "merge")));
        Assert.Equal(conflictLocal, await HeadAsync(checkout));
        Assert.Equal(conflictRemote, (await sample.GitAsync(checkout, token, "rev-parse", "MERGE_HEAD")).Trim());
        Assert.Equal("git_pull_in_progress", Code(await SyncAsync("git_pull", conflictLocal, "merge")));
        Assert.Empty(await sample.GitAsync(checkout, token, "for-each-ref", "refs/retro-ide/pull"));
        Assert.Equal(sibling, await HeadAsync(sample.Linked));

        async Task<string> HeadAsync(string repo) => (await sample.GitAsync(repo, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        async Task<string> RemoteTipAsync() => (await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/main").ConfigureAwait(false)).Trim();
        static string? Code(JsonElement result) => result.GetProperty("code").GetString();

        async Task EditAsync(string text) => await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = view.Capture(), ["offset"] = 0, ["length"] = view.Buffer.TextLength, ["text"] = text,
        });

        async Task<JsonElement> SyncAsync(string tool, string expected, string? strategy = null, string? lease = null, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
            var args = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout, ["name"] = "origin", ["revision"] = revision,
                ["branchName"] = "main", ["expectedHead"] = expected, ["remoteBranch"] = "main",
                ["context"] = context.GetProperty("context"),
            };
            if (strategy is not null)
                args["strategy"] = strategy;
            if (lease is not null)
            {
                args["forceWithLease"] = true;
                args["expectedRemoteHead"] = lease;
            }
            return await connection.CallAsync(tool, args, transport).ConfigureAwait(false);
        }
    }
}
