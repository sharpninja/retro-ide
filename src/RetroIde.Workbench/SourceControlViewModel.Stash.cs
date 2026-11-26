using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class SourceControlViewModel
{
    internal Func<CancellationToken, Task>? PromptStashesAsync { get; set; }

    [ObservableProperty]
    public partial GitStash.Stack? Stashes { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectStashCommand), nameof(ApplyStashCommand), nameof(PopStashCommand), nameof(DropStashCommand))]
    public partial GitStash.Entry? SelectedStash { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateStashCommand))]
    public partial string? StashMessage { get; set; }
    [ObservableProperty]
    public partial bool IncludeUntrackedInStash { get; set; }
    [ObservableProperty]
    public partial bool RestoreStashIndex { get; set; }
    [ObservableProperty]
    public partial string? StashPatch { get; private set; }

    internal Control WrapStashes(Control content) => Interaction.Wrap(PaneId, content);
    partial void OnSelectedStashChanged(GitStash.Entry? value) => StashPatch = null;
    private bool HasStashSelection() => SelectedStash is not null;
    private bool CanCreateStash() => !string.IsNullOrWhiteSpace(StashMessage);

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task OpenStashesAsync(CancellationToken token)
    {
        SetStashes(null);
        StashMessage = null;
        IncludeUntrackedInStash = false;
        RestoreStashIndex = false;
        await RefreshStashesAsync(token).ConfigureAwait(true);
        if (Stashes is not null && PromptStashesAsync is { } prompt && !token.IsCancellationRequested)
            await prompt(token).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshStashesAsync(CancellationToken token)
    {
        if (Repository is not { } repo)
            return;
        var result = await RequestAsync(Git.ListStashes, new GitStash.ListInput(repo.WorktreePath), token).ConfigureAwait(true);
        if (result is { Code: "ok", Data: { } data })
            SetStashes(data.Deserialize<GitStash.Stack>(WireJson.Options));
    }

    [RelayCommand(CanExecute = nameof(HasStashSelection))]
    private async Task InspectStashAsync(CancellationToken token)
    {
        if (EntryInput() is not { } input)
            return;
        var result = await RequestAsync(Git.InspectStash, input, token).ConfigureAwait(true);
        if (EntryInput() == input && result is { Code: "ok", Data: { } data })
            StashPatch = data.Deserialize<GitStash.Inspection>(WireJson.Options)?.Patch;
    }

    [RelayCommand(CanExecute = nameof(CanCreateStash))]
    private async Task CreateStashAsync(CancellationToken token)
    {
        if (Stashes is not { } stack || string.IsNullOrWhiteSpace(StashMessage))
            return;
        if (await MutateStashAsync(Git.CreateStash, new GitStash.CreateInput(stack.Repository.WorktreePath, StashMessage,
            IncludeUntrackedInStash), token).ConfigureAwait(true))
            StashMessage = null;
    }

    [RelayCommand(CanExecute = nameof(HasStashSelection))]
    private Task ApplyStashAsync(CancellationToken token) => MutateEntryAsync(Git.ApplyStash, token);
    [RelayCommand(CanExecute = nameof(HasStashSelection))]
    private Task PopStashAsync(CancellationToken token) => MutateEntryAsync(Git.PopStash, token);
    [RelayCommand(CanExecute = nameof(HasStashSelection))]
    private Task DropStashAsync(CancellationToken token) => MutateEntryAsync(Git.DropStash, token);

    private Task MutateEntryAsync(Feature<GitStash.EntryInput> feature, CancellationToken token) =>
        EntryInput() is { } input ? MutateStashAsync(feature, input, token) : Task.CompletedTask;
    private GitStash.EntryInput? EntryInput() => Stashes is { } stack && SelectedStash is { } item
        ? new GitStash.EntryInput(stack.Repository.WorktreePath, stack.Revision, item.Selector, item.ObjectId, RestoreStashIndex) : null;

    private async Task<bool> MutateStashAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        Interaction.Begin(PaneId);
        try
        {
            var result = await RequestAsync(feature, input, token).ConfigureAwait(true);
            if (result?.Data is { } data && data.Deserialize<GitStash.Result>(WireJson.Options) is { } stash)
                SetStashes(stash.Stack);
            return result?.Code is "ok";
        }
        finally { Interaction.End(PaneId); }
    }

    private void SetStashes(GitStash.Stack? stack)
    {
        SelectedStash = null;
        StashPatch = null;
        Stashes = stack;
    }
}
