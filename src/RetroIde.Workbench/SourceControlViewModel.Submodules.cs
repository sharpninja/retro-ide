using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class SourceControlViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSubmodules))]
    public partial GitSubmoduleState.Module[]? Submodules { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitSubmoduleCommand), nameof(UpdateSubmoduleCommand), nameof(SyncSubmoduleCommand))]
    public partial GitSubmoduleState.Module? SelectedSubmodule { get; set; }

    public bool HasNoSubmodules => Submodules is { Length: 0 };
    internal Func<CancellationToken, Task>? PromptSubmodulesAsync { get; set; }
    internal Control WrapSubmodules(Control content) => Interaction.Wrap(PaneId, content);

    private bool CanInitSubmodule() => CanRefreshStatus() && SelectedSubmodule is { CanChange: true, IsConfigured: false };
    private bool CanUpdateSubmodule() => CanRefreshStatus() && SelectedSubmodule is { CanChange: true };
    private bool CanSyncSubmodule() => CanRefreshStatus() && SelectedSubmodule is { CanChange: true, IsConfigured: true };

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task OpenSubmodulesAsync(CancellationToken token)
    {
        Submodules = null;
        SelectedSubmodule = null;
        await RefreshSubmodulesAsync(token).ConfigureAwait(true);
        if (Submodules is not null && PromptSubmodulesAsync is { } prompt && !token.IsCancellationRequested)
            await prompt(token).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task RefreshSubmodulesAsync(CancellationToken token)
    {
        if (Repository is not { } repo)
            return;
        var path = SelectedSubmodule?.Path;
        var result = await RequestAsync(Git.ListSubmodules, new GitSubmoduleOperations.ListInput(repo.WorktreePath), token).ConfigureAwait(true);
        if (ReferenceEquals(Repository, repo) && result is { Code: "ok", Data: { } data } &&
            data.Deserialize<GitSubmoduleState.Module[]>(WireJson.Options) is { } modules)
        {
            Submodules = modules;
            SelectedSubmodule = Array.Find(modules, item => string.Equals(item.Path, path, StringComparison.Ordinal));
        }
    }

    [RelayCommand(CanExecute = nameof(CanInitSubmodule))]
    private Task InitSubmoduleAsync(CancellationToken token) => MutateSubmoduleAsync(Git.InitSubmodule, token);

    [RelayCommand(CanExecute = nameof(CanUpdateSubmodule))]
    private Task UpdateSubmoduleAsync(CancellationToken token) => MutateSubmoduleAsync(Git.UpdateSubmodule, token);

    [RelayCommand(CanExecute = nameof(CanSyncSubmodule))]
    private Task SyncSubmoduleAsync(CancellationToken token) => MutateSubmoduleAsync(Git.SyncSubmodule, token);

    private async Task MutateSubmoduleAsync(Feature<GitSubmoduleOperations.TargetInput> feature, CancellationToken token)
    {
        if (Repository is not { } repo || SelectedSubmodule is not { CanChange: true } module)
            return;
        var input = new GitSubmoduleOperations.TargetInput(repo.WorktreePath, module.Path, module.Revision);
        Interaction.Begin(PaneId);
        try
        {
            var result = await RequestAsync(feature, input, token).ConfigureAwait(true);
            if (ReferenceEquals(Repository, repo) && result is { Code: "ok", Data: { } data } &&
                data.Deserialize<GitSubmoduleState.Module>(WireJson.Options) is { } updated && Submodules is { } modules)
            {
                Submodules = modules.Select(item => string.Equals(item.Path, updated.Path, StringComparison.Ordinal) ? updated : item).ToArray();
                SelectedSubmodule = updated;
            }
        }
        finally { Interaction.End(PaneId); }
    }

    private void NotifySubmoduleCommands()
    {
        OpenSubmodulesCommand.NotifyCanExecuteChanged();
        RefreshSubmodulesCommand.NotifyCanExecuteChanged();
        InitSubmoduleCommand.NotifyCanExecuteChanged();
        UpdateSubmoduleCommand.NotifyCanExecuteChanged();
        SyncSubmoduleCommand.NotifyCanExecuteChanged();
    }
}
