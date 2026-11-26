using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitHistoryViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWorktreeCommand), nameof(LockWorktreeCommand), nameof(UnlockWorktreeCommand),
        nameof(RemoveWorktreeCommand))]
    public partial GitHistoryOperations.Worktree? SelectedWorktree { get; set; }

    internal Func<GitWorktreeDialog.Prompt, CancellationToken, Task<GitWorktreeDialog.Choice?>>? PromptWorktreeAsync { get; set; }

    private bool CanRefreshWorktrees() => !_changingBranch;
    private bool CanAddWorktree() => !_changingBranch && Page?.Anchor is not null;
    private bool CanOpenWorktree() => !_changingBranch && SelectedWorktree is { IsBare: false, IsPrunable: false, Head: not null };
    private bool CanLockWorktree() => CanOpenWorktree() && SelectedWorktree is { IsPrimary: false, IsLocked: false };
    private bool CanUnlockWorktree() => CanOpenWorktree() && SelectedWorktree is { IsPrimary: false, IsLocked: true };
    private bool CanRemoveWorktree() => CanLockWorktree() && SelectedWorktree is { } row &&
        !NativeGit.ContainsPath(row.Path, Page.Repository.WorktreePath);

    [RelayCommand(CanExecute = nameof(CanRefreshWorktrees))]
    private async Task RefreshWorktreesAsync(CancellationToken token)
    {
        var selected = SelectedWorktree;
        var path = Page.Repository.WorktreePath;
        var result = await RequestAsync(_git.ListWorktrees, new GitWorktreeOperations.ListInput(path), token).ConfigureAwait(true);
        if (result is not { Code: "ok", Data: { } data } || token.IsCancellationRequested)
            return;
        var rows = data.Deserialize<GitHistoryOperations.Worktree[]>(WireJson.Options);
        if (rows is null)
            return;
        References = References with { Worktrees = rows };
        if (selected is not null)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            SelectedWorktree = Array.Find(rows, row => string.Equals(row.Path, selected.Path, comparison));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddWorktree))]
    private async Task AddWorktreeAsync(CancellationToken token)
    {
        if (!CanAddWorktree() || PromptWorktreeAsync is not { } prompt ||
            (SelectedCommit?.Commit.Id ?? Page.Anchor) is not { } commit)
            return;
        var path = Page.Repository.WorktreePath;
        const string prefix = "refs/heads/";
        var branch = SelectedReference is { } reference && reference.Name.StartsWith(prefix, StringComparison.Ordinal)
            ? reference : null;
        var choice = await prompt(new(WorkbenchCommands.AddGitWorktree, commit, branch), token).ConfigureAwait(true);
        if (choice?.Text is not { } destination || token.IsCancellationRequested)
            return;
        var input = choice.UseExistingBranch && branch is not null
            ? new GitWorktreeOperations.AddInput(path, destination, branch.ObjectId, ExistingBranch: branch.Name[prefix.Length..])
            : new GitWorktreeOperations.AddInput(path, destination, commit, choice.NewBranch);
        await RunHistoryMutationAsync(_git.AddWorktree, input, token).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanOpenWorktree))]
    private async Task OpenWorktreeAsync(CancellationToken token)
    {
        if (!CanOpenWorktree() || SelectedWorktree is not { Head: { } head } row)
            return;
        var result = await RequestAsync(_git.OpenWorktree, Review(row, head), token).ConfigureAwait(true);
        if (result is { Code: "ok" } && !token.IsCancellationRequested)
        {
            await RequestAsync(_git.OpenHistory, new GitHistoryOperations.HistoryInput(row.Path), token).ConfigureAwait(true);
            LastResult = result;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLockWorktree))]
    private Task LockWorktreeAsync(CancellationToken token) => PromptMaintenanceAsync(WorkbenchCommands.LockGitWorktree, _git.LockWorktree, token);

    [RelayCommand(CanExecute = nameof(CanUnlockWorktree))]
    private Task UnlockWorktreeAsync(CancellationToken token) => SelectedWorktree is { Head: { } head } row && CanUnlockWorktree()
        ? RunHistoryMutationAsync(_git.UnlockWorktree, Review(row, head), token) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRemoveWorktree))]
    private Task RemoveWorktreeAsync(CancellationToken token) => PromptMaintenanceAsync(WorkbenchCommands.RemoveGitWorktree, _git.RemoveWorktree, token);

    private async Task PromptMaintenanceAsync(CommandDescriptor descriptor, Feature<GitWorktreeOperations.TargetInput> feature, CancellationToken token)
    {
        if (SelectedWorktree is not { Head: { } head } row || PromptWorktreeAsync is not { } prompt)
            return;
        var review = Review(row, head);
        var answer = await prompt(new(descriptor, head, Target: row), token).ConfigureAwait(true);
        if (answer is not null && !token.IsCancellationRequested)
            await RunHistoryMutationAsync(feature, review with { Reason = answer.Text }, token).ConfigureAwait(true);
    }

    private GitWorktreeOperations.TargetInput Review(GitHistoryOperations.Worktree row, string head) =>
        new(Page.Repository.WorktreePath, row.Path, head, row.IsLocked, row.Branch);

    private void NotifyWorktreeCommands()
    {
        RefreshWorktreesCommand.NotifyCanExecuteChanged();
        AddWorktreeCommand.NotifyCanExecuteChanged();
        OpenWorktreeCommand.NotifyCanExecuteChanged();
        LockWorktreeCommand.NotifyCanExecuteChanged();
        UnlockWorktreeCommand.NotifyCanExecuteChanged();
        RemoveWorktreeCommand.NotifyCanExecuteChanged();
    }
}
