using System.Reflection;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitWorkspaceStateTests
{
    /// <summary>Reopens the saved worktree independently of Explorer selection and rejects a missing checkout.</summary>
    [AvaloniaFact]
    public async Task Desktop_restores_the_selected_worktree_and_preserves_other_roots()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var folder = Directory.CreateTempSubdirectory("retro-workspace-git-state-");
        try
        {
            var store = new WorkspaceExplorerStore(Path.Combine(folder.FullName, "workspace.json"));
            Guid workspace;
            Guid explorer;
            await using (var before = new WorkbenchSession(store, null))
            {
                var window = new WorkbenchWindow(before);
                window.Show();
                await using var connection = await WorkspaceConnection.ConnectAsync(before, token);
                workspace = before.View.WorkspaceId;
                explorer = await connection.AddRootAsync(sample.Primary);
                var linked = await connection.AddRootAsync(sample.Linked);
                await connection.ChangeAsync("git_select_repository", Address(linked, GitWorktreeFixture.ChangedPath));
                await connection.ChangeAsync("workspace_select_entry", Address(explorer, ""));
                await CloseAsync(window, token);
            }

            // A file used to locate the worktree is transient. The saved identity belongs to its checkout.
            File.Delete(Path.Combine(sample.Linked, GitWorktreeFixture.ChangedPath));
            await using (var reopened = new WorkbenchSession(store, null))
            {
                var window = new WorkbenchWindow(reopened);
                window.Show();
                try
                {
                    await ObserveRestoreAsync(reopened, token);
                    await using var connection = await WorkspaceConnection.ConnectAsync(reopened, token);
                    var status = await connection.CallAsync("git_get_status");
                    Assert.True(string.Equals("ok", status.GetProperty("code").GetString(), StringComparison.Ordinal),
                        "Desktop did not restore the captured worktree.");
                    VerifySelection(new Selection(reopened.View.WorkspaceId, reopened.Explorer.Snapshot().Selection!.Value.RootId,
                        status.GetProperty("data").GetProperty("repository").GetProperty("worktreePath").GetString()), workspace, explorer, sample.Linked);
                    Assert.Equal(sample.Linked, reopened.SourceControl.Repository?.WorktreePath, NativePaths);
                }
                finally { await CloseAsync(window, token); }
            }

            Directory.Move(sample.Linked, sample.Linked + "-removed");
            await using var missing = new WorkbenchSession(store, null);
            await ObserveRestoreAsync(missing, token);
            await using var client = await WorkspaceConnection.ConnectAsync(missing, token);
            var absent = await client.CallAsync("git_get_status");
            Assert.Equal("git_selection_required", absent.GetProperty("code").GetString());
            VerifySelection(new Selection(missing.View.WorkspaceId, missing.Explorer.Snapshot().Selection!.Value.RootId,
                missing.SourceControl.Repository?.WorktreePath), workspace, explorer, worktree: null);
            Assert.Equal(2, missing.Explorer.Roots.Count);
            var remaining = await client.CallAsync("workspace_list_children", Address(explorer, ""));
            Assert.True(remaining.GetProperty("data").GetProperty("isAvailable").GetBoolean());
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Shows status on real Explorer rows and clears a prior checkout's badges when selection changes.</summary>
    [AvaloniaFact]
    public async Task Explorer_decorates_only_the_selected_Git_worktree()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        await CreateConflictAsync(sample, token);
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "staged\n");
        await sample.GitAsync(sample.Primary, token, "add", "--", GitWorktreeFixture.ChangedPath);
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "staged and changed again\n");
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.HunkPath, "working change\n");
        GitWorktreeFixture.Write(sample.Primary, ".gitignore", "ignored.data\n");
        GitWorktreeFixture.Write(sample.Primary, "untracked.asm", "new file\n");
        GitWorktreeFixture.Write(sample.Primary, "ignored.data", "ignored\n");
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await using var connection = await WorkspaceConnection.ConnectAsync(session, token);
            var primary = await connection.AddRootAsync(sample.Primary);
            var linked = await connection.AddRootAsync(sample.Linked);
            await connection.ExpandAsync(primary, "");
            await connection.ExpandAsync(primary, "src");
            await connection.ExpandAsync(linked, "");
            await connection.ExpandAsync(linked, "src");
            await connection.ChangeAsync("git_select_repository", Address(primary, ""));
            Assert.Equal("ok", (await connection.CallAsync("git_get_status")).GetProperty("code").GetString());
            VerifyBadges(new Badges(await ReadBadgeAsync(window, primary, GitWorktreeFixture.ConflictPath, token),
                await ReadBadgeAsync(window, primary, GitWorktreeFixture.ChangedPath, token),
                await ReadBadgeAsync(window, primary, GitWorktreeFixture.HunkPath, token),
                await ReadBadgeAsync(window, primary, "untracked.asm", token),
                await ReadBadgeAsync(window, primary, "ignored.data", token),
                await ReadBadgeAsync(window, primary, "conflict.asm", token),
                await ReadBadgeAsync(window, linked, GitWorktreeFixture.ChangedPath, token)));

            await connection.ChangeAsync("git_select_repository", Address(linked, ""));
            Assert.Empty(await ReadBadgeAsync(window, primary, GitWorktreeFixture.ChangedPath, token));
            Assert.Equal("ok", (await connection.CallAsync("git_get_status")).GetProperty("code").GetString());
            Assert.Equal("Tracked", await ReadBadgeAsync(window, linked, GitWorktreeFixture.ChangedPath, token));

            var limited = await connection.AddRootAsync(Path.Combine(sample.Primary, "src"));
            await connection.ChangeAsync("git_select_repository", Address(limited, ""));
            var status = (await connection.CallAsync("git_get_status")).GetProperty("data");
            Assert.Equal(2, status.GetProperty("trackedPaths").GetArrayLength());
            Assert.All(status.GetProperty("trackedPaths").EnumerateArray(), path =>
                Assert.StartsWith("src/", path.GetString(), StringComparison.Ordinal));
            Assert.Empty(await ReadBadgeAsync(window, primary, "conflict.asm", token));
        }
        finally { window.Close(); }
    }

    private static async Task CreateConflictAsync(GitWorktreeFixture sample, CancellationToken token)
    {
        const string Conflict = "conflict.asm";
        GitWorktreeFixture.Write(sample.Peer, Conflict, "peer version\n");
        await sample.GitAsync(sample.Peer, token, "add", "--", Conflict).ConfigureAwait(false);
        await sample.GitAsync(sample.Peer, token, "commit", "-m", "Add peer source").ConfigureAwait(false);
        GitWorktreeFixture.Write(sample.Primary, Conflict, "primary version\n");
        await sample.GitAsync(sample.Primary, token, "add", "--", Conflict).ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "commit", "-m", "Add primary source").ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "fetch", "--no-tags", sample.Peer, "HEAD").ConfigureAwait(false);
        var conflict = await sample.RunAsync(sample.Primary, token, "merge", "--no-edit", "FETCH_HEAD").ConfigureAwait(false);
        Assert.Equal(1, conflict.ExitCode);
    }

    private static async Task ObserveRestoreAsync(WorkbenchSession session, CancellationToken token)
    {
        if (typeof(WorkbenchSession).GetProperty("GitRestoration", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session) is Task restore)
            await restore.WaitAsync(token).ConfigureAwait(true);
    }

    private static async Task CloseAsync(Window window, CancellationToken token)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(token).ConfigureAwait(true);
    }

    private static async Task<string> ReadBadgeAsync(Window window, Guid root, string path, CancellationToken token)
    {
        var relative = Path.GetRelativePath(Path.GetTempPath(), Path.GetFullPath(path, Path.GetTempPath()));
        while (true)
        {
            token.ThrowIfCancellationRequested();
            window.UpdateLayout();
            var row = window.GetVisualDescendants().OfType<TreeViewItem>().SingleOrDefault(item =>
                item.DataContext is ExplorerTreeNode { Address: { } address } &&
                address.RootId == root && NativePaths.Equals(address.RelativePath, relative));
            if (row is not null)
                return row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text =>
                    string.Equals(AutomationProperties.GetAutomationId(text), "workspace.git-status", StringComparison.Ordinal))?.Text ?? "";
            await Task.Delay(20, token).ConfigureAwait(true);
        }
    }

    private static Dictionary<string, object?> Address(Guid root, string path) =>
        new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = path };

    internal sealed class WorkspaceConnection(LoopbackMcpHost host, AttachmentDescriptor attachment, McpClient client,
        HttpClient http, CancellationToken token) : IAsyncDisposable
    {
        internal static async Task<WorkspaceConnection> ConnectAsync(WorkbenchSession session, CancellationToken token, bool canMutate = true)
        {
            var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token).ConfigureAwait(false);
            var attachment = host.CreateAttachment("workspace-git-state", canMutate);
            var http = attachment.CreateHttpClient();
            try
            {
                var client = await McpClient.CreateAsync(new HttpClientTransport(new()
                {
                    Endpoint = attachment.Endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                }, http), cancellationToken: token).ConfigureAwait(false);
                return new(host, attachment, client, http, token);
            }
            catch
            {
                http.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task<Guid> AddRootAsync(string path)
        {
            var result = await ChangeAsync("workspace_open_root", new(StringComparer.Ordinal) { ["path"] = path, ["kind"] = "folder" }).ConfigureAwait(false);
            return result.GetProperty("data").GetProperty("id").GetGuid();
        }

        internal Task<JsonElement> ExpandAsync(Guid root, string path)
        {
            var args = Address(root, path);
            args["isExpanded"] = true;
            return ChangeAsync("workspace_set_expanded", args);
        }

        internal async Task<McpClient> ConnectStdioAsync(string file, bool shareGrant = false)
        {
            var descriptor = shareGrant ? attachment : host.CreateAttachment("workspace-git-stdio", canMutate: true);
            descriptor.Write(file);
            return await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = "dotnet",
                Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, file],
            }), cancellationToken: token).ConfigureAwait(false);
        }

        internal void Revoke() => host.Revoke(attachment);

        internal async Task<JsonElement> ChangeAsync(string name, Dictionary<string, object?> args, McpClient? transport = null)
        {
            var context = await CallAsync("workbench_get_context").ConfigureAwait(false);
            args["context"] = context.GetProperty("context");
            var result = await CallAsync(name, args, transport).ConfigureAwait(false);
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        internal async Task<JsonElement> CallAsync(string name, Dictionary<string, object?>? args = null, McpClient? transport = null)
        {
            var response = await (transport ?? client).CallToolAsync(name, args, cancellationToken: token).ConfigureAwait(false);
            Assert.NotNull(response.StructuredContent);
            return response.StructuredContent.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await client.DisposeAsync().ConfigureAwait(false);
            http.Dispose();
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
