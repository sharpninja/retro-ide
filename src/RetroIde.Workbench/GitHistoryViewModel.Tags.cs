using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitHistoryViewModel
{
    internal Func<GitTagDialog.Prompt, CancellationToken, Task<GitTagDialog.Choice?>>? PromptTagAsync { get; set; }

    private bool CanInspectTag() => !_changingBranch &&
        SelectedReference?.Name.StartsWith(GitTagOperations.Prefix, StringComparison.Ordinal) == true;

    private bool CanDeleteTag() => CanInspectTag() && SelectedReference?.SymbolicTarget is null;

    [RelayCommand(CanExecute = nameof(CanApplyCommit))]
    private async Task CreateTagAsync(CancellationToken token)
    {
        if (SelectedCommit?.Commit is not { } target || PromptTagAsync is not { } prompt)
            return;

        var path = Page.Repository.WorktreePath;
        var choice = await prompt(new GitTagDialog.Prompt(target.Id), token).ConfigureAwait(true);
        if (choice is null || token.IsCancellationRequested)
            return;
        await RunHistoryMutationAsync(_git.CreateTag, new GitTagOperations.CreateInput(path, choice.Name, target.Id, choice.Message), token)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanInspectTag))]
    private Task InspectTagAsync(CancellationToken token) => ReviewTagAsync(delete: false, token);

    [RelayCommand(CanExecute = nameof(CanDeleteTag))]
    private Task DeleteTagAsync(CancellationToken token) => ReviewTagAsync(delete: true, token);

    private async Task ReviewTagAsync(bool delete, CancellationToken token)
    {
        if (SelectedReference is not { } reference || !CanInspectTag() || PromptTagAsync is not { } prompt)
            return;

        var path = Page.Repository.WorktreePath;
        var name = reference.Name[GitTagOperations.Prefix.Length..];
        var result = await RequestAsync(_git.InspectTag, new GitTagOperations.ReadInput(path, name, reference.ObjectId), token).ConfigureAwait(true);
        if (result is not { Code: "ok", Data: { } data } || token.IsCancellationRequested ||
            data.Deserialize<GitTagOperations.Tag>(WireJson.Options) is not { } tag)
            return;

        var choice = await prompt(new GitTagDialog.Prompt(tag.TargetObjectId, tag, delete), token).ConfigureAwait(true);
        if (delete && choice is not null && !token.IsCancellationRequested)
            await RunHistoryMutationAsync(_git.DeleteTag, new GitTagOperations.DeleteInput(path, tag.TagName, tag.ObjectId), token)
                .ConfigureAwait(true);
    }
}
