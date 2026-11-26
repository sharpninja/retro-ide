using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitHistoryViewModel
{
    private bool _changingBranch;
    internal Func<string, CancellationToken, Task<GitBranchDialog.Choice?>>? PromptBranchAsync { get; set; }

    private bool CanCreateBranch() => !_changingBranch && Page?.Anchor is not null;
    private bool CanSwitchBranch() => !_changingBranch && SelectedReference?.Name.StartsWith("refs/heads/", StringComparison.Ordinal) == true;

    [RelayCommand(CanExecute = nameof(CanCreateBranch))]
    private async Task CreateBranchAsync(CancellationToken token)
    {
        var start = SelectedCommit?.Commit.Id ?? Page.Anchor;
        if (start is null || PromptBranchAsync is not { } prompt)
            return;

        var checkout = Page.Repository.WorktreePath;
        if (await prompt(start, token).ConfigureAwait(true) is not { } choice || token.IsCancellationRequested)
            return;

        await ChangeBranchAsync(_git.CreateBranch, new GitBranchOperations.CreateInput(checkout, choice.Name, start, choice.Switch), token)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private Task SwitchBranchAsync(CancellationToken token) => SelectedReference is { } reference && CanSwitchBranch()
        ? ChangeBranchAsync(_git.SwitchBranch, new GitBranchOperations.SwitchInput(Page.Repository.WorktreePath,
            reference.Name["refs/heads/".Length..], reference.ObjectId), token)
        : Task.CompletedTask;

    private async Task ChangeBranchAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
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
    }
}
