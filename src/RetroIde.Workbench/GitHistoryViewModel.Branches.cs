using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitHistoryViewModel
{
    private bool _changingBranch;
    internal Func<GitBranchDialog.Prompt, CancellationToken, Task<GitBranchDialog.Choice?>>? PromptBranchAsync { get; set; }
    internal Func<GitHistoryOperations.Worktree, GitHistoryOperations.Commit, CancellationToken, Task<GitStartOperationDialog.Choice?>>? PromptStartAsync { get; set; }
    internal Func<GitReset.Review, CancellationToken, Task<GitResetDialog.Choice?>>? PromptResetAsync { get; set; }

    private bool CanCreateBranch() => !_changingBranch && Page?.Anchor is not null;
    private bool CanSwitchBranch() => !_changingBranch && SelectedReference?.Name.StartsWith("refs/heads/", StringComparison.Ordinal) == true;
    private bool CanApplyCommit() => !_changingBranch && SelectedCommit is not null;

    [RelayCommand(CanExecute = nameof(CanApplyCommit))]
    private async Task StartOperationAsync(CancellationToken token)
    {
        if (SelectedCommit?.Commit is not { } target || PromptStartAsync is not { } prompt)
            return;
        var path = Page.Repository.WorktreePath;
        var result = await RequestAsync(_git.Refs, new GitHistoryOperations.RefsInput(path), token).ConfigureAwait(true);
        if (result is not { Code: "ok", Data: { } data } ||
            data.Deserialize<GitHistoryOperations.RefState>(WireJson.Options) is not { } refs ||
            GitHistoryOperations.CurrentWorktree(refs) is not { Head: { } head } checkout ||
            await prompt(checkout, target, token).ConfigureAwait(true) is not { } choice || token.IsCancellationRequested)
            return;

        await RunHistoryMutationAsync(_git.StartOperation, new GitOperationStart.Input(path, choice.Kind, head,
            target.Id, checkout.Branch, choice.Mainline), token).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanApplyCommit))]
    private async Task ResetToCommitAsync(CancellationToken token)
    {
        if (SelectedCommit?.Commit is not { } target || PromptResetAsync is not { } prompt)
            return;
        var path = Page.Repository.WorktreePath;
        var response = await RequestAsync(_git.ReviewReset, new GitReset.ReviewInput(path, target.Id), token).ConfigureAwait(true);
        if (response is not { Code: "ok", Data: { } data } ||
            data.Deserialize<GitReset.Review>(WireJson.Options) is not { } review ||
            token.IsCancellationRequested || await prompt(review, token).ConfigureAwait(true) is not { } choice ||
            token.IsCancellationRequested)
            return;

        await RunHistoryMutationAsync(_git.Reset, new GitReset.ResetInput(path, review.TargetCommit, review.Revision, choice.Mode), token)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCreateBranch))]
    private async Task CreateBranchAsync(CancellationToken token)
    {
        var start = SelectedCommit?.Commit.Id ?? Page.Anchor;
        if (start is null || PromptBranchAsync is not { } prompt)
            return;

        var checkout = Page.Repository.WorktreePath;
        if (await prompt(new GitBranchDialog.Prompt(start), token).ConfigureAwait(true) is not { } choice || token.IsCancellationRequested)
            return;

        await RunHistoryMutationAsync(_git.CreateBranch, new GitBranchOperations.CreateInput(checkout, choice.Name, start, choice.Switch), token)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private Task SwitchBranchAsync(CancellationToken token) => SelectedReference is { } reference && CanSwitchBranch()
        ? RunHistoryMutationAsync(_git.SwitchBranch, new GitBranchOperations.SwitchInput(Page.Repository.WorktreePath,
            reference.Name["refs/heads/".Length..], reference.ObjectId), token)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private Task RenameBranchAsync(CancellationToken token) => MaintainBranchAsync(delete: false, token);

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private Task DeleteBranchAsync(CancellationToken token) => MaintainBranchAsync(delete: true, token);

    private async Task MaintainBranchAsync(bool delete, CancellationToken token)
    {
        if (SelectedReference is not { } reference || !CanSwitchBranch() || PromptBranchAsync is not { } prompt)
            return;

        var worktree = Page.Repository.WorktreePath;
        var name = reference.Name["refs/heads/".Length..];
        var choice = await prompt(new GitBranchDialog.Prompt(name, name, delete), token).ConfigureAwait(true);
        if (choice is null || token.IsCancellationRequested)
            return;

        if (delete)
            await RunHistoryMutationAsync(_git.DeleteBranch, new GitBranchOperations.DeleteInput(worktree, name, reference.ObjectId), token).ConfigureAwait(true);
        else
            await RunHistoryMutationAsync(_git.RenameBranch, new GitBranchOperations.RenameInput(worktree, name, reference.ObjectId, choice.Name), token)
                .ConfigureAwait(true);
    }

    private async Task RunHistoryMutationAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        _changingBranch = true;
        NotifyBranchCommands();
        _interaction.Begin(Scope);
        try
        {
            var result = await RequestAsync(feature, input, token).ConfigureAwait(true);
            if (result is { Code: "ok" })
            {
                await CurrentHistoryAsync(token).ConfigureAwait(true);
                LastResult = result;
            }
        }
        finally
        {
            _interaction.End(Scope);
            _changingBranch = false;
            NotifyBranchCommands();
        }
    }

    private void NotifyBranchCommands()
    {
        CreateBranchCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        SetUpstreamCommand.NotifyCanExecuteChanged();
        StartOperationCommand.NotifyCanExecuteChanged();
        ResetToCommitCommand.NotifyCanExecuteChanged();
        CreateTagCommand.NotifyCanExecuteChanged();
        InspectTagCommand.NotifyCanExecuteChanged();
        DeleteTagCommand.NotifyCanExecuteChanged();
    }
}
