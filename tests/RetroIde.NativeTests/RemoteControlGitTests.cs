using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;
using RetroIde.Application.Git;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlGitTests
{
    /// <summary>Exercises the Source Control commands and staged diff in a native desktop without physical input.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        const string Change = "native Source Control change";
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, Change + "\n");
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1280, Height = 900 };
        window.Show();
        try
        {
            var platform = window.TryGetPlatformHandle() ?? throw new InvalidOperationException("Git verification requires a native window.");
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token).ConfigureAwait(true);
            var attachment = host.CreateAttachment("native-git-workbench", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token).ConfigureAwait(true);

            var opened = await MutateAsync("workspace_open_root", new(StringComparer.Ordinal)
            {
                ["path"] = sample.Primary,
                ["kind"] = "folder",
            }).ConfigureAwait(true);
            _ = await MutateAsync("git_select_repository", new(StringComparer.Ordinal)
            {
                ["rootId"] = opened.GetProperty("data").GetProperty("id").GetGuid(),
                ["relativePath"] = "",
            }).ConfigureAwait(true);
            _ = await MutateAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "source-control" }).ConfigureAwait(true);
            _ = await InvokeAsync("git_get_status").ConfigureAwait(true);

            var list = await FindAsync<ListBox>(window, "git.status.working", token).ConfigureAwait(true);
            list.SelectedItem = ((IEnumerable<GitStatusEntry>)list.ItemsSource!).Single(item =>
                string.Equals(item.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
            await WorkbenchRemoteControl.ClickAsync(window, "git.file.stage.pane").ConfigureAwait(true);
            await (session.SourceControl.StageFileCommand.ExecutionTask ??
                throw new InvalidOperationException("Source Control did not dispatch Stage File.")).WaitAsync(token).ConfigureAwait(true);
            _ = await InvokeAsync("git_get_status").ConfigureAwait(true);
            var staged = await FindAsync<ListBox>(window, "git.status.staged", token).ConfigureAwait(true);
            staged.SelectedItem = ((IEnumerable<GitStatusEntry>)staged.ItemsSource!).Single(item =>
                string.Equals(item.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
            await WorkbenchRemoteControl.ClickAsync(window, "git.diff.open.pane").ConfigureAwait(true);
            await (session.SourceControl.OpenDiffCommand.ExecutionTask ??
                throw new InvalidOperationException("Source Control did not dispatch Open Diff.")).WaitAsync(token).ConfigureAwait(true);
            var editor = await FindAsync<TextEditor>(window, "git.diff.patch", token).ConfigureAwait(true);
            if (!editor.IsReadOnly || !editor.Text.Contains("+" + Change, StringComparison.Ordinal))
                throw new InvalidOperationException("Source Control did not present the staged patch in a read-only native tab.");
            Console.WriteLine($"RemoteControl staged an exact Git path and opened its read-only diff on {platform.HandleDescriptor} without physical input.");
            await VerifyHunkAsync(window, sample, token).ConfigureAwait(true);
            _ = await MutateAsync("git_stage_file", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary,
                ["path"] = GitWorktreeFixture.ChangedPath,
            }).ConfigureAwait(true);
            await VerifyCommitAsync(window, session, sample, token).ConfigureAwait(true);
            await VerifyHistoryAsync(window, session, token).ConfigureAwait(true);
            await VerifyBranchAsync(window, sample, token).ConfigureAwait(true);
            await VerifyBranchMaintenanceAsync(window, sample, token).ConfigureAwait(true);
            await VerifyRemoteSyncAsync(window, sample, token).ConfigureAwait(true);
            _ = await MutateAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "source-control" }).ConfigureAwait(true);
            await VerifyRecoveryAsync(window, session, sample, token).ConfigureAwait(true);

            async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> arguments)
            {
                var current = await InvokeAsync("workbench_get_context").ConfigureAwait(true);
                arguments["context"] = current.GetProperty("context");
                return await InvokeAsync(tool, arguments).ConfigureAwait(true);
            }

            async Task<JsonElement> InvokeAsync(string tool, Dictionary<string, object?>? arguments = null)
            {
                var response = await client.CallToolAsync(tool, arguments, cancellationToken: token).ConfigureAwait(true);
                if (response.StructuredContent is not { } result ||
                    !string.Equals(result.GetProperty("code").GetString(), "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Native Git tool {tool} failed.");
                return result;
            }
        }
        finally { window.Close(); }
    }

    private static async Task VerifyHunkAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        var list = await FindAsync<ListBox>(window, "git.diff.hunks", token).ConfigureAwait(true);
        list.SelectedItem = ((IEnumerable<GitHunkPatch.Hunk>)list.ItemsSource!).Single();
        const string ButtonId = "git.hunks.unstage.toolbar";
        var button = await FindAsync<Button>(window, ButtonId, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, ButtonId).ConfigureAwait(true);
        await ((IAsyncRelayCommand)button.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        var diff = await sample.GitAsync(sample.Primary, token, "diff", "--cached", "--", GitWorktreeFixture.ChangedPath).ConfigureAwait(true);
        if (diff.Length != 0)
            throw new InvalidOperationException("Native Unstage Selected Hunks did not update the expected index.");
        Console.WriteLine("RemoteControl unstaged a selected text hunk from the native diff tab.");
    }

    private static async Task VerifyCommitAsync(Window window, WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var checkout = sample.Primary;
        var hooks = Directory.CreateDirectory(Path.Combine(checkout, ".git", "native-test-hooks"));
        await sample.GitAsync(checkout, token, "config", "--local", "core.hooksPath", hooks.FullName).ConfigureAwait(true);
        await sample.GitAsync(checkout, token, "config", "--local", "user.name", "Retro Native Test").ConfigureAwait(true);
        await sample.GitAsync(checkout, token, "config", "--local", "user.email", "native@example.invalid").ConfigureAwait(true);
        await sample.GitAsync(checkout, token, "config", "--local", "commit.gpgsign", "false").ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, "git.commit.create.pane").ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        const string Message = "Native workbench commit";
        var field = await FindAsync<TextBox>(dialog, "git.commit.message", token).ConfigureAwait(true);
        field.Text = Message;
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.commit.submit").ConfigureAwait(true);
        await session.SourceControl.CommitCommand.ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        var actual = await sample.GitAsync(checkout, token, "log", "-1", "--format=%s").ConfigureAwait(true);
        if (!string.Equals(Message, actual.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("The native Git dialog did not commit the staged state.");
        Console.WriteLine("RemoteControl committed the reviewed native Git index through the workbench dialog.");
    }

    private static async Task VerifyHistoryAsync(Window window, WorkbenchSession session, CancellationToken token)
    {
        await WorkbenchRemoteControl.ClickAsync(window, "git.history.open.pane").ConfigureAwait(true);
        await session.SourceControl.OpenHistoryCommand.ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        var graph = await FindAsync<ListBox>(window, "git.history.graph", token).ConfigureAwait(true);
        var row = ((IEnumerable<GitHistoryGraph.Row>)graph.ItemsSource!).First();
        if (!string.Equals(row.Commit.Subject, "Native workbench commit", StringComparison.Ordinal))
            throw new InvalidOperationException("The native Git graph did not show the selected checkout's new HEAD.");
        graph.SelectedItem = row;
        const string Inspect = "git.history.commit-details.toolbar";
        var button = await FindAsync<Button>(window, Inspect, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, Inspect).ConfigureAwait(true);
        await ((IAsyncRelayCommand)button.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        var message = await FindAsync<TextBlock>(window, "git.history.commit-message", token).ConfigureAwait(true);
        if (!string.Equals(message.Text, row.Commit.Subject, StringComparison.Ordinal))
            throw new InvalidOperationException("The native Git inspector did not show the selected commit.");
        Console.WriteLine("RemoteControl opened the native Git history graph and inspected the selected commit.");
    }

    private static async Task VerifyBranchAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        const string Create = "git.branch.create.toolbar";
        var create = await FindAsync<Button>(window, Create, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, Create).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var name = await FindAsync<TextBox>(dialog, "git.branch.name", token).ConfigureAwait(true);
        name.Text = "topic/native-workbench";
        var checkout = await FindAsync<CheckBox>(dialog, "git.branch.checkout", token).ConfigureAwait(true);
        checkout.IsChecked = true;
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.branch.submit").ConfigureAwait(true);
        await ((IAsyncRelayCommand)create.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        var branch = (await sample.GitAsync(sample.Primary, token, "branch", "--show-current").ConfigureAwait(true)).Trim();
        if (!string.Equals(branch, "topic/native-workbench", StringComparison.Ordinal))
            throw new InvalidOperationException("The native Git branch dialog did not create and switch to the requested branch.");
        Console.WriteLine("RemoteControl created and checked out a local branch through the native Git History dialog.");
    }

    private static async Task VerifyBranchMaintenanceAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        const string Original = "topic/native-maintenance";
        const string Renamed = "topic/native-renamed";
        await sample.GitAsync(sample.Primary, token, "branch", "--no-track", Original).ConfigureAwait(true);
        await (await ClickCommandAsync("git.history.current.toolbar").ConfigureAwait(true)).WaitAsync(token).ConfigureAwait(true);
        await SelectAsync(Original).ConfigureAwait(true);
        var rename = await ClickCommandAsync("git.branch.rename.toolbar").ConfigureAwait(true);
        var dialog = await WaitForDialogAsync().ConfigureAwait(true);
        var name = await FindAsync<TextBox>(dialog, "git.branch.name", token).ConfigureAwait(true);
        name.Text = Renamed;
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.branch.submit").ConfigureAwait(true);
        await rename.WaitAsync(token).ConfigureAwait(true);
        await SelectAsync(Renamed).ConfigureAwait(true);
        var delete = await ClickCommandAsync("git.branch.delete.toolbar").ConfigureAwait(true);
        dialog = await WaitForDialogAsync().ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.branch.submit").ConfigureAwait(true);
        await delete.WaitAsync(token).ConfigureAwait(true);
        var refs = await sample.GitAsync(sample.Primary, token, "branch", "--list", Original, Renamed).ConfigureAwait(true);
        if (refs.Length != 0)
            throw new InvalidOperationException("The native Git controls did not rename and safely delete the requested branch.");
        Console.WriteLine("RemoteControl renamed and safely deleted a merged local branch in native Git History.");

        async Task<Task> ClickCommandAsync(string id)
        {
            var button = await FindAsync<Button>(window, id, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            return ((IAsyncRelayCommand)button.Command!).ExecutionTask!;
        }

        async Task SelectAsync(string branch)
        {
            var list = await FindAsync<ListBox>(window, "git.history.refs", token).ConfigureAwait(true);
            list.SelectedItem = ((IEnumerable<GitHistoryOperations.Reference>)list.ItemsSource!).Single(reference =>
                string.Equals(reference.Name, "refs/heads/" + branch, StringComparison.Ordinal));
        }

        async Task<Window> WaitForDialogAsync()
        {
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            return window.OwnedWindows.Single();
        }
    }

    private static async Task VerifyRemoteSyncAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Native remote-only update").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main").ConfigureAwait(true);
        var tip = await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/main").ConfigureAwait(true);
        var before = await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true);
        const string Open = "git.remotes.open.toolbar";
        var open = await FindAsync<Button>(window, Open, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, Open).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var list = await FindAsync<ListBox>(dialog, "git.remotes.list", token).ConfigureAwait(true);
        list.SelectedItem = ((IEnumerable<GitRemoteOperations.Remote>)list.ItemsSource!).Single(remote =>
            string.Equals(remote.Name, "origin", StringComparison.Ordinal));
        await ClickAsync("git.remote.fetch.toolbar").ConfigureAwait(true);
        var available = await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/remotes/origin/main").ConfigureAwait(true);
        var after = await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true);
        if (!string.Equals(tip, available, StringComparison.Ordinal) || !string.Equals(before, after, StringComparison.Ordinal))
            throw new InvalidOperationException("The Git Remotes dialog did not fetch or unexpectedly changed local HEAD.");
        Console.WriteLine("RemoteControl fetched a native tracking update without moving the local branch.");
        var branch = await FindAsync<TextBox>(dialog, "git.remotes.branch", token).ConfigureAwait(true);
        branch.Text = "native-push";
        await ClickAsync("git.remote.push.toolbar").ConfigureAwait(true);
        var pushed = await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/native-push").ConfigureAwait(true);
        if (!string.Equals(before, pushed, StringComparison.Ordinal))
            throw new InvalidOperationException("The Git Remotes dialog did not push the reviewed tip to the requested branch.");
        Console.WriteLine("RemoteControl pushed the checked-out branch to the requested remote ref.");

        await sample.GitAsync(sample.Peer, token, "fetch", "origin", "native-push").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "switch", "-c", "native-pull", "origin/native-push").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Native pull update").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "push", "origin", "native-pull").ConfigureAwait(true);
        var incoming = await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/native-pull").ConfigureAwait(true);
        branch.Text = "native-pull";
        await ClickAsync("git.remote.pull.toolbar").ConfigureAwait(true);
        var pulled = await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true);
        if (!string.Equals(incoming, pulled, StringComparison.Ordinal))
            throw new InvalidOperationException("The Git Remotes dialog did not fast-forward the checked-out branch.");
        Console.WriteLine("RemoteControl pulled and fast-forwarded the checked-out branch.");

        await WorkbenchRemoteControl.ClickAsync(dialog, "git.remotes.close").ConfigureAwait(true);
        await ((IAsyncRelayCommand)open.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);

        async Task ClickAsync(string id)
        {
            var button = await FindAsync<Button>(dialog, id, token).ConfigureAwait(true);
            button.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException($"The Git Remotes dialog did not dispatch {id}.")).WaitAsync(token).ConfigureAwait(true);
        }
    }

    private static async Task VerifyRecoveryAsync(Window window, WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var worktree = sample.Primary;
        var path = GitWorktreeFixture.ConflictPath;
        const string Original = "native local branch\n";
        GitWorktreeFixture.Write(worktree, path, Original);
        GitWorktreeFixture.Write(sample.Linked, path, "native linked branch\n");
        await CommitSideAsync(worktree, "Local side").ConfigureAwait(true);
        await CommitSideAsync(sample.Linked, "Linked side").ConfigureAwait(true);
        var before = (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        var incoming = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await RunButtonAsync("git.history.open.pane").ConfigureAwait(true);
        var refs = await FindAsync<ListBox>(window, "git.history.refs", token).ConfigureAwait(true);
        refs.SelectedItem = ((IEnumerable<GitHistoryOperations.Reference>)refs.ItemsSource!).Single(reference =>
            string.Equals(reference.Name, "refs/heads/fixture-linked", StringComparison.Ordinal));
        await RunButtonAsync("git.history.ref.toolbar").ConfigureAwait(true);
        var graph = await FindAsync<ListBox>(window, "git.history.graph", token).ConfigureAwait(true);
        graph.SelectedItem = ((IEnumerable<GitHistoryGraph.Row>)graph.ItemsSource!).Single(row =>
            string.Equals(row.Commit.Id, incoming, StringComparison.Ordinal));
        const string Start = "git.operation.start.toolbar";
        var start = await FindAsync<Button>(window, Start, token).ConfigureAwait(true);
        start.BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, Start).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var operation = await FindAsync<ComboBox>(dialog, "git.operation.start.kind", token).ConfigureAwait(true);
        if (operation.SelectedItem is not ComboBoxItem { Tag: "merge" })
            throw new InvalidOperationException("The operation dialog did not present Merge.");
        if (!dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => string.Equals(text.Text, incoming, StringComparison.Ordinal)))
            throw new InvalidOperationException("The operation dialog did not review the selected target commit.");
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.operation.start.submit").ConfigureAwait(true);
        await (((IAsyncRelayCommand)start.Command!).ExecutionTask ??
            throw new InvalidOperationException("The start operation command was not dispatched.")).WaitAsync(token).ConfigureAwait(true);
        var mergeHead = (await sample.GitAsync(worktree, token, "rev-parse", "--verify", "MERGE_HEAD").ConfigureAwait(true)).Trim();
        if (!string.Equals(incoming, mergeHead, StringComparison.Ordinal))
            throw new InvalidOperationException("The Git History command did not merge the selected commit.");
        Console.WriteLine("RemoteControl started the reviewed Git History merge and exposed the conflict in Source Control.");

        var banner = await FindAsync<TextBlock>(window, "git.operation.status", token).ConfigureAwait(true);
        var resume = await FindAsync<Button>(window, "git.operation.continue.toolbar", token).ConfigureAwait(true);
        while (resume.IsEffectivelyEnabled || !session.SourceControl.Conflicts.Any(entry =>
            string.Equals(entry.Path, path, StringComparison.Ordinal)))
            await Task.Delay(20, token).ConfigureAwait(true);
        if (!string.Equals(banner.Text, "Merge in progress", StringComparison.Ordinal))
            throw new InvalidOperationException($"Source Control showed an unexpected Git operation label: {banner.Text}");

        await ConfirmAsync("git.operation.abort.cancel").ConfigureAwait(true);
        _ = await sample.GitAsync(worktree, token, "rev-parse", "--verify", "MERGE_HEAD").ConfigureAwait(true);
        await ConfirmAsync("git.operation.abort.confirm").ConfigureAwait(true);
        if ((await sample.RunAsync(worktree, token, "rev-parse", "--verify", "MERGE_HEAD").ConfigureAwait(true)).ExitCode == 0 ||
            !string.Equals(before, (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim(), StringComparison.Ordinal) ||
            !string.Equals(Original, await File.ReadAllTextAsync(Path.Combine(worktree, path), token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmed Abort did not restore the selected Git worktree.");
        Console.WriteLine("RemoteControl identified a native merge conflict, canceled an abort, then confirmed recovery in the selected checkout.");

        async Task CommitSideAsync(string root, string message)
        {
            await sample.GitAsync(root, token, "add", "--", path).ConfigureAwait(true);
            await sample.GitAsync(root, token, "commit", "-m", message).ConfigureAwait(true);
        }

        async Task RunButtonAsync(string id)
        {
            var button = await FindAsync<Button>(window, id, token).ConfigureAwait(true);
            button.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException($"The Git control did not dispatch {id}.")).WaitAsync(token).ConfigureAwait(true);
        }

        async Task ConfirmAsync(string id)
        {
            const string Abort = "git.operation.abort.toolbar";
            var abort = await FindAsync<Button>(window, Abort, token).ConfigureAwait(true);
            abort.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, Abort).ConfigureAwait(true);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(window.OwnedWindows.Single(), id).ConfigureAwait(true);
            await (session.SourceControl.AbortOperationCommand.ExecutionTask ??
                throw new InvalidOperationException("The Abort command was not dispatched.")).WaitAsync(token).ConfigureAwait(true);
        }
    }

    private static async Task<T> FindAsync<T>(Window window, string id, CancellationToken token) where T : Control
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            window.UpdateLayout();
            var control = window.GetVisualDescendants().OfType<T>().SingleOrDefault(item =>
                item.IsEffectivelyVisible && string.Equals(AutomationProperties.GetAutomationId(item), id, StringComparison.Ordinal));
            if (control is not null)
                return control;
            await Task.Delay(20, token).ConfigureAwait(true);
        }
    }
}
