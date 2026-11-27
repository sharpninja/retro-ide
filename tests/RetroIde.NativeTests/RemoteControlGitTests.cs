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

internal static partial class RemoteControlGitTests
{
    /// <summary>Exercises the Source Control commands and staged diff in a native desktop without physical input.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push, ac-test-git-extensions; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
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
            await VerifyBlameAsync(window, session.SourceControl, Change, token).ConfigureAwait(true);
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
            await VerifyTagsAsync(window, sample, token).ConfigureAwait(true);
            await VerifyWorktreesAsync(window, session, sample, token).ConfigureAwait(true);
            await VerifyBranchAsync(window, sample, token).ConfigureAwait(true);
            await VerifyBranchMaintenanceAsync(window, sample, token).ConfigureAwait(true);
            await VerifyRemoteSyncAsync(window, sample, token).ConfigureAwait(true);
            _ = await MutateAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "source-control" }).ConfigureAwait(true);
            await VerifyRecoveryAsync(window, session, sample, token).ConfigureAwait(true);
            await VerifyStashAsync(window, sample, token).ConfigureAwait(true);
            await VerifyPatchAsync(window, session.SourceControl, sample, token).ConfigureAwait(true);
            using var submoduleDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await VerifySubmodulesAsync(window, session.SourceControl, sample, submoduleDeadline.Token).ConfigureAwait(true);
            using var lfsDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await VerifyLfsAsync(window, session.SourceControl, sample, lfsDeadline.Token).ConfigureAwait(true);
            await VerifyTerminalAsync(window, session, sample).ConfigureAwait(true);

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
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            throw;
        }
        finally { window.Close(); }
    }

    private static async Task VerifyTagsAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        const string TagName = "native-checkpoint";
        const string Message = "Annotated by the native workbench.";
        await sample.GitAsync(sample.Primary, token, "config", "--local", "tag.gpgSign", "false").ConfigureAwait(true);
        var graph = await FindAsync<ListBox>(window, "git.history.graph", token).ConfigureAwait(true);
        var commit = ((IEnumerable<GitHistoryGraph.Row>)graph.ItemsSource!).First();
        graph.SelectedItem = commit;

        var (create, createCommand) = await OpenAsync("git.tag.create.toolbar").ConfigureAwait(true);
        (await FindAsync<TextBox>(create, "git.tag.name", token).ConfigureAwait(true)).Text = TagName;
        (await FindAsync<TextBox>(create, "git.tag.message", token).ConfigureAwait(true)).Text = Message;
        await AnswerAsync(create, createCommand, "git.tag.submit").ConfigureAwait(true);
        var referenceName = GitTagOperations.Prefix + TagName;
        var target = await sample.GitAsync(sample.Primary, token, "rev-parse", referenceName + "^{}").ConfigureAwait(true);
        if (!string.Equals(target.Trim(), commit.Commit.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("New Tag did not use the selected commit.");

        var refs = await FindAsync<ListBox>(window, "git.history.refs", token).ConfigureAwait(true);
        refs.SelectedItem = ((IEnumerable<GitHistoryOperations.Reference>)refs.ItemsSource!).Single(reference =>
            string.Equals(reference.Name, referenceName, StringComparison.Ordinal));
        var (inspect, inspectCommand) = await OpenAsync("git.tag.read.toolbar").ConfigureAwait(true);
        var annotation = await FindAsync<TextBox>(inspect, "git.tag.annotation", token).ConfigureAwait(true);
        if (!annotation.IsReadOnly || !string.Equals(annotation.Text, Message, StringComparison.Ordinal))
            throw new InvalidOperationException("Inspect Tag did not show the native annotation.");
        await AnswerAsync(inspect, inspectCommand, "git.tag.cancel").ConfigureAwait(true);

        var (cancel, cancelCommand) = await OpenAsync("git.tag.delete.toolbar").ConfigureAwait(true);
        await AnswerAsync(cancel, cancelCommand, "git.tag.cancel").ConfigureAwait(true);
        _ = await sample.GitAsync(sample.Primary, token, "rev-parse", "--verify", referenceName).ConfigureAwait(true);
        var (confirm, confirmCommand) = await OpenAsync("git.tag.delete.toolbar").ConfigureAwait(true);
        await AnswerAsync(confirm, confirmCommand, "git.tag.submit").ConfigureAwait(true);
        if ((await sample.RunAsync(sample.Primary, token, "rev-parse", "--verify", referenceName).ConfigureAwait(true)).ExitCode == 0)
            throw new InvalidOperationException("Delete Tag left the reviewed tag in the repository.");
        Console.WriteLine("RemoteControl created and inspected an annotated Git tag, cancelled its removal, and then confirmed deletion.");

        async Task<(Window Dialog, IAsyncRelayCommand Command)> OpenAsync(string id)
        {
            var button = await FindAsync<Button>(window, id, token).ConfigureAwait(true);
            button.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            return (window.OwnedWindows.Single(), (IAsyncRelayCommand)button.Command!);
        }

        async Task AnswerAsync(Window dialog, IAsyncRelayCommand command, string id)
        {
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await (command.ExecutionTask ?? throw new InvalidOperationException("The tag command did not dispatch."))
                .WaitAsync(token).ConfigureAwait(true);
        }
    }

    private static async Task VerifyBlameAsync(Window window, SourceControlViewModel view, string expected, CancellationToken token)
    {
        const string Open = "git.blame.open.pane";
        (await FindAsync<Button>(window, Open, token).ConfigureAwait(true)).BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, Open).ConfigureAwait(true);
        await (view.OpenBlameCommand.ExecutionTask ??
            throw new InvalidOperationException("Source Control did not dispatch Open Blame.")).WaitAsync(token).ConfigureAwait(true);
        var lines = await FindAsync<ListBox>(window, "git.blame.lines", token).ConfigureAwait(true);
        var row = ((IEnumerable<GitBlameOperations.Line>)lines.ItemsSource!).Single();
        if (!row.Uncommitted || !string.Equals(row.Text, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Git Blame did not present the selected working file's attribution.");
        lines.SelectedItem = row;
        var detail = await FindAsync<SelectableTextBlock>(window, "git.blame.details", token).ConfigureAwait(true);
        if (detail.Text?.Contains(expected, StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Git Blame did not expose the selected line's full source text.");
        Console.WriteLine("RemoteControl opened working-file Git blame and inspected native line attribution without physical input.");
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

        var skip = await FindAsync<Button>(window, "git.operation.skip.toolbar", token).ConfigureAwait(true);
        if (skip.IsEffectivelyEnabled)
            throw new InvalidOperationException("Source Control enabled Skip for a merge.");
        await ConfirmAsync("git.operation.abort", "cancel").ConfigureAwait(true);
        _ = await sample.GitAsync(worktree, token, "rev-parse", "--verify", "MERGE_HEAD").ConfigureAwait(true);
        await ConfirmAsync("git.operation.abort", "confirm").ConfigureAwait(true);
        if ((await sample.RunAsync(worktree, token, "rev-parse", "--verify", "MERGE_HEAD").ConfigureAwait(true)).ExitCode == 0 ||
            !string.Equals(before, (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim(), StringComparison.Ordinal) ||
            !string.Equals(Original, await File.ReadAllTextAsync(Path.Combine(worktree, path), token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmed Abort did not restore the selected Git worktree.");
        Console.WriteLine("RemoteControl identified a native merge conflict, canceled an abort, then confirmed recovery in the selected checkout.");

        if ((await sample.RunAsync(worktree, token, "cherry-pick", incoming).ConfigureAwait(true)).ExitCode == 0)
            throw new InvalidOperationException("The Skip fixture did not pause on a cherry-pick conflict.");
        while (session.SourceControl.OperationKind is not "cherry-pick")
            await Task.Delay(20, token).ConfigureAwait(true);
        await ConfirmAsync("git.operation.skip", "cancel", incoming).ConfigureAwait(true);
        _ = await sample.GitAsync(worktree, token, "rev-parse", "--verify", "CHERRY_PICK_HEAD").ConfigureAwait(true);
        await ConfirmAsync("git.operation.skip", "confirm", incoming).ConfigureAwait(true);
        if ((await sample.RunAsync(worktree, token, "rev-parse", "--verify", "CHERRY_PICK_HEAD").ConfigureAwait(true)).ExitCode == 0 ||
            !string.Equals(before, (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim(), StringComparison.Ordinal) ||
            !string.Equals(Original, await File.ReadAllTextAsync(Path.Combine(worktree, path), token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmed Skip did not omit the reviewed cherry-pick step.");
        Console.WriteLine("RemoteControl reviewed and skipped a paused cherry-pick without physical input.");

        await ReviewResetAsync("git.reset.cancel").ConfigureAwait(true);
        if (!string.Equals(before, (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Cancel changed HEAD during reset review.");
        await ReviewResetAsync("git.reset.submit").ConfigureAwait(true);
        if (!string.Equals(incoming, (await sample.GitAsync(worktree, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim(), StringComparison.Ordinal) ||
            !string.Equals("native linked branch\n", await File.ReadAllTextAsync(Path.Combine(worktree, path), token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmed hard reset did not restore the reviewed commit.");
        Console.WriteLine("RemoteControl reviewed a hard reset, canceled it, then reset the selected checkout to the reviewed commit.");

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

        async Task ConfirmAsync(string command, string answer, string? expectedCommit = null)
        {
            var id = command + ".toolbar";
            var button = await FindAsync<Button>(window, id, token).ConfigureAwait(true);
            while (!button.IsEffectivelyEnabled)
                await Task.Delay(20, token).ConfigureAwait(true);
            button.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            var confirmation = window.OwnedWindows.Single();
            await FindAsync<Button>(confirmation, command + "." + answer, token).ConfigureAwait(true);
            if (expectedCommit is not null && !confirmation.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Any(text => text.IsEffectivelyVisible && string.Equals(text.Text, expectedCommit, StringComparison.Ordinal)))
                throw new InvalidOperationException("The Skip confirmation did not identify the pending commit.");
            await WorkbenchRemoteControl.ClickAsync(confirmation, command + "." + answer).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException($"The recovery command {command} was not dispatched.")).WaitAsync(token).ConfigureAwait(true);
        }

        async Task ReviewResetAsync(string answer)
        {
            const string Id = "git.reset.commit.toolbar";
            var button = await FindAsync<Button>(window, Id, token).ConfigureAwait(true);
            button.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, Id).ConfigureAwait(true);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            var resetDialog = window.OwnedWindows.Single();
            var mode = await FindAsync<ComboBox>(resetDialog, "git.reset.mode", token).ConfigureAwait(true);
            if (mode.SelectedItem is not ComboBoxItem { Tag: "mixed" })
                throw new InvalidOperationException("Reset review did not default to Mixed.");
            mode.SelectedItem = ((IEnumerable<ComboBoxItem>)mode.ItemsSource!).Single(item => item.Tag is "hard");
            var effect = await FindAsync<TextBlock>(resetDialog, "git.reset.effect", token).ConfigureAwait(true);
            if (effect.Text?.Contains("tracked files", StringComparison.Ordinal) != true ||
                !resetDialog.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => string.Equals(text.Text, incoming, StringComparison.Ordinal)))
                throw new InvalidOperationException("Reset review did not show the target commit and hard reset effect.");
            await WorkbenchRemoteControl.ClickAsync(resetDialog, answer).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException("Reset review was not dispatched.")).WaitAsync(token).ConfigureAwait(true);
        }
    }

    private static async Task VerifyStashAsync(Window window, GitWorktreeFixture sample, CancellationToken token)
    {
        var checkout = sample.Primary;
        var file = GitWorktreeFixture.HunkPath;
        var path = Path.Combine(checkout, file);
        var baseline = await File.ReadAllTextAsync(path, token).ConfigureAwait(true);
        const string Extra = "native stash untracked.txt";
        GitWorktreeFixture.Write(checkout, file, "native staged stash text\n");
        await sample.GitAsync(checkout, token, "add", "--", file).ConfigureAwait(true);
        GitWorktreeFixture.Write(checkout, file, "native working stash text\n");
        GitWorktreeFixture.Write(checkout, Extra, "native untracked stash text\n");

        const string Open = "git.stash.open.pane";
        var open = await FindAsync<Button>(window, Open, token).ConfigureAwait(true);
        open.BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, Open).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var message = await FindAsync<TextBox>(dialog, "git.stash.message", token).ConfigureAwait(true);
        message.Text = "Native shelf";
        (await FindAsync<CheckBox>(dialog, "git.stash.untracked", token).ConfigureAwait(true)).IsChecked = true;
        await InvokeAsync("git.stash.create.toolbar").ConfigureAwait(true);
        if (File.Exists(Path.Combine(checkout, Extra)) ||
            !string.Equals(baseline, await File.ReadAllTextAsync(path, token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("Stash creation did not clear the reviewed worktree changes.");
        var list = await FindAsync<ListBox>(dialog, "git.stash.entries", token).ConfigureAwait(true);
        list.SelectedItem = ((IEnumerable<GitStash.Entry>)list.ItemsSource!).Single();
        await InvokeAsync("git.stash.inspect.toolbar").ConfigureAwait(true);
        var patch = await FindAsync<TextBox>(dialog, "git.stash.patch", token).ConfigureAwait(true);
        if (!patch.IsReadOnly || patch.Text?.Contains("native working stash text", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("The selected stash did not show its read-only patch.");
        (await FindAsync<CheckBox>(dialog, "git.stash.index", token).ConfigureAwait(true)).IsChecked = true;
        await InvokeAsync("git.stash.pop.toolbar").ConfigureAwait(true);
        if (!string.Equals("native staged stash text\n", await sample.GitAsync(checkout, token, "show", ":" + file).ConfigureAwait(true), StringComparison.Ordinal) ||
            !string.Equals("native working stash text\n", await File.ReadAllTextAsync(path, token).ConfigureAwait(true), StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(checkout, Extra)) || (await sample.GitAsync(checkout, token, "stash", "list").ConfigureAwait(true)).Length != 0)
            throw new InvalidOperationException("The native Pop did not restore the stash layers and remove the applied entry.");
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.stash.close").ConfigureAwait(true);
        await ((IAsyncRelayCommand)open.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        Console.WriteLine("RemoteControl created, inspected and popped a native Git stash with staged, working and untracked changes.");

        async Task InvokeAsync(string id)
        {
            var button = await FindAsync<Button>(dialog, id, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException($"The stash control did not dispatch {id}.")).WaitAsync(token).ConfigureAwait(true);
        }
    }

    private static async Task VerifyPatchAsync(Window window, SourceControlViewModel view, GitWorktreeFixture sample, CancellationToken token)
    {
        var checkout = sample.Primary;
        var file = GitWorktreeFixture.HunkPath;
        var path = Path.Combine(checkout, file);
        var working = await File.ReadAllTextAsync(path, token).ConfigureAwait(true);
        byte[]? patch = null;
        view.SavePatchAsync = (bytes, _) =>
        {
            patch = bytes;
            return Task.FromResult(true);
        };
        view.PickPatchAsync = _ => Task.FromResult(patch is null ? null : new SourceControlViewModel.PatchFile("changes.patch", patch));
        const string Open = "git.patch.open.pane";
        var open = await FindAsync<Button>(window, Open, token).ConfigureAwait(true);
        open.BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, Open).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var layers = await FindAsync<ComboBox>(dialog, "git.patch.layer", token).ConfigureAwait(true);
        if (layers.SelectedItem is not ComboBoxItem { Tag: "index" })
            throw new InvalidOperationException("Git patches did not default to the index.");
        layers.SelectedItem = ((IEnumerable<ComboBoxItem>)layers.ItemsSource!).Single(item => item.Tag is "worktree");
        await InvokeAsync("git.patch.export.toolbar").ConfigureAwait(true);
        if (patch is not { Length: > 0 })
            throw new InvalidOperationException("The worktree patch was not exported.");
        await sample.GitAsync(checkout, token, "restore", "--worktree", "--", file).ConfigureAwait(true);
        await InvokeAsync("git.patch.review.toolbar").ConfigureAwait(true);
        var paths = await FindAsync<ListBox>(dialog, "git.patch.paths", token).ConfigureAwait(true);
        if (((IEnumerable<string>?)paths.ItemsSource)?.Contains(file, StringComparer.Ordinal) != true)
            throw new InvalidOperationException("The patch review did not show the affected worktree path.");
        await InvokeAsync("git.patch.apply.toolbar").ConfigureAwait(true);
        if (!string.Equals(working, await File.ReadAllTextAsync(path, token).ConfigureAwait(true), StringComparison.Ordinal))
            throw new InvalidOperationException("The reviewed patch did not restore the native working file.");
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.patch.close").ConfigureAwait(true);
        await ((IAsyncRelayCommand)open.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
        Console.WriteLine("RemoteControl exported, reviewed and applied a native Git worktree patch.");

        async Task InvokeAsync(string id)
        {
            var button = await FindAsync<Button>(dialog, id, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ??
                throw new InvalidOperationException($"The patch control did not dispatch {id}.")).WaitAsync(token).ConfigureAwait(true);
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
