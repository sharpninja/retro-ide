using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlGitTests
{
    private static async Task VerifyWorktreesAsync(Window window, WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var path = Path.Combine(Path.GetDirectoryName(sample.Primary)!, "native linked checkout");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var (add, addCommand) = await OpenDialogAsync("git.worktree.add.toolbar").ConfigureAwait(true);
        (await FindAsync<TextBox>(add, "git.worktree.destination", token).ConfigureAwait(true)).Text = path;
        await AnswerAsync(add, addCommand, "git.worktree.submit").ConfigureAwait(true);
        var detached = await SelectAsync(path).ConfigureAwait(true);
        if (!detached.IsDetached || detached.IsPrimary || detached.IsLocked || !File.Exists(Path.Combine(path, ".git")))
            throw new InvalidOperationException("Add Worktree did not create a detached linked checkout.");

        var (locking, lockCommand) = await OpenDialogAsync("git.worktree.lock.toolbar").ConfigureAwait(true);
        (await FindAsync<TextBox>(locking, "git.worktree.reason", token).ConfigureAwait(true)).Text = "Native lock";
        await AnswerAsync(locking, lockCommand, "git.worktree.submit").ConfigureAwait(true);
        var locked = await SelectAsync(path).ConfigureAwait(true);
        if (!locked.IsLocked || !string.Equals(locked.LockReason, "Native lock", StringComparison.Ordinal))
            throw new InvalidOperationException("Worktree list did not show the native lock reason.");
        await ClickAsync("git.worktree.unlock.toolbar").ConfigureAwait(true);
        if ((await SelectAsync(path).ConfigureAwait(true)).IsLocked)
            throw new InvalidOperationException("Unlock Worktree left the native lock in place.");

        await ClickAsync("git.worktree.open.toolbar").ConfigureAwait(true);
        if (!session.Explorer.Roots.Any(root => string.Equals(root.Path, path, comparison)) ||
            !string.Equals(session.SourceControl.Repository?.WorktreePath, path, comparison))
            throw new InvalidOperationException("Open Worktree did not activate the linked checkout in Source Control and Explorer.");
        _ = await SelectAsync(sample.Primary).ConfigureAwait(true);
        await ClickAsync("git.worktree.open.toolbar").ConfigureAwait(true);
        _ = await SelectAsync(path).ConfigureAwait(true);
        var (cancel, cancelCommand) = await OpenDialogAsync("git.worktree.remove.toolbar").ConfigureAwait(true);
        await AnswerAsync(cancel, cancelCommand, "git.worktree.cancel").ConfigureAwait(true);
        if (!Directory.Exists(path))
            throw new InvalidOperationException("Cancelling Remove Worktree changed the checkout.");
        var (confirm, confirmCommand) = await OpenDialogAsync("git.worktree.remove.toolbar").ConfigureAwait(true);
        await AnswerAsync(confirm, confirmCommand, "git.worktree.submit").ConfigureAwait(true);
        if (Directory.Exists(path) || session.Explorer.Roots.Any(root => string.Equals(root.Path, path, comparison)))
            throw new InvalidOperationException("Confirmed Remove Worktree did not remove the native checkout and its Explorer root.");
        Console.WriteLine("RemoteControl added, locked, unlocked, opened and removed a native Git worktree after verifying cancellation.");

        async Task<GitHistoryOperations.Worktree> SelectAsync(string target)
        {
            var list = await FindAsync<ListBox>(window, "git.history.worktrees", token).ConfigureAwait(true);
            var row = ((IEnumerable<GitHistoryOperations.Worktree>)list.ItemsSource!).Single(entry =>
                string.Equals(entry.Path, target, comparison));
            list.SelectedItem = row;
            return row;
        }

        async Task ClickAsync(string id)
        {
            var command = await ActivateAsync(id).ConfigureAwait(true);
            await CompleteAsync(command).ConfigureAwait(true);
        }

        async Task<(Window Dialog, IAsyncRelayCommand Command)> OpenDialogAsync(string id)
        {
            var command = await ActivateAsync(id).ConfigureAwait(true);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            return (window.OwnedWindows.Single(), command);
        }

        async Task<IAsyncRelayCommand> ActivateAsync(string id)
        {
            var action = await FindAsync<Button>(window, id, token).ConfigureAwait(true);
            action.BringIntoView();
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            return (IAsyncRelayCommand)action.Command!;
        }

        async Task AnswerAsync(Window dialog, IAsyncRelayCommand command, string id)
        {
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await CompleteAsync(command).ConfigureAwait(true);
        }

        Task CompleteAsync(IAsyncRelayCommand command) =>
            (command.ExecutionTask ?? throw new InvalidOperationException("The worktree command did not dispatch.")).WaitAsync(token);
    }
}
