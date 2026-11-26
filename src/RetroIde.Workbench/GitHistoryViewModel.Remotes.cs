using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitHistoryViewModel
{
    internal Func<CancellationToken, Task>? PromptRemotesAsync { get; set; }
    internal Func<string, GitHistoryOperations.Reference[], CancellationToken, Task<GitUpstreamDialog.Choice?>>? PromptUpstreamAsync { get; set; }

    [ObservableProperty]
    public partial GitRemoteOperations.RemoteList? RemoteSettings { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditRemoteCommand), nameof(RenameRemoteCommand), nameof(RemoveRemoteCommand), nameof(FetchRemoteCommand))]
    public partial GitRemoteOperations.Remote? SelectedRemote { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRemoteCommand), nameof(RenameRemoteCommand))]
    public partial string? RemoteName { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRemoteCommand), nameof(EditRemoteCommand))]
    public partial string? RemoteUrl { get; set; }
    [ObservableProperty]
    public partial bool EditPushUrl { get; set; }
    [ObservableProperty]
    public partial GitRemoteOperations.TrackingStatus? LastFetchStatus { get; private set; }

    internal Control WrapRemoteSurface(Control content) => _interaction.Wrap(Scope, content);

    private bool CanAddRemote() => !string.IsNullOrWhiteSpace(RemoteName) && !string.IsNullOrWhiteSpace(RemoteUrl);
    private bool CanEditRemote() => SelectedRemote is not null && !string.IsNullOrWhiteSpace(RemoteUrl);
    private bool CanRenameRemote() => SelectedRemote is not null && !string.IsNullOrWhiteSpace(RemoteName);
    private bool HasRemoteSelection() => SelectedRemote is not null;

    [RelayCommand]
    private async Task ShowRemotesAsync(CancellationToken token)
    {
        await RefreshRemotesAsync(token).ConfigureAwait(true);
        if (RemoteSettings is null || PromptRemotesAsync is not { } prompt || token.IsCancellationRequested)
            return;
        await prompt(token).ConfigureAwait(true);
        await CurrentHistoryAsync(token).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshRemotesAsync(CancellationToken token)
    {
        RemoteSettings = null;
        var result = await RequestAsync(_git.Remotes, new GitRemoteOperations.ListInput(Page.Repository.WorktreePath), token).ConfigureAwait(true);
        if (result is { Code: "ok", Data: { } data })
            RemoteSettings = data.Deserialize<GitRemoteOperations.RemoteList>(WireJson.Options);
    }

    [RelayCommand(CanExecute = nameof(CanAddRemote))]
    private Task AddRemoteAsync(CancellationToken token) => ConfigureRemoteAsync(_git.AddRemote, RemoteName, RemoteUrl, token);

    [RelayCommand(CanExecute = nameof(CanEditRemote))]
    private Task EditRemoteAsync(CancellationToken token) => ConfigureRemoteAsync(_git.SetRemoteUrl, SelectedRemote?.Name, RemoteUrl, token);

    [RelayCommand(CanExecute = nameof(CanRenameRemote))]
    private Task RenameRemoteAsync(CancellationToken token) => ConfigureRemoteAsync(_git.RenameRemote, SelectedRemote?.Name, RemoteName, token);

    [RelayCommand(CanExecute = nameof(HasRemoteSelection))]
    private Task RemoveRemoteAsync(CancellationToken token) => ConfigureRemoteAsync(_git.RemoveRemote, SelectedRemote?.Name, null, token);

    private async Task ConfigureRemoteAsync(Feature<GitRemoteOperations.ConfigureInput> feature, string? name, string? value, CancellationToken token)
    {
        if (name is null || RemoteSettings is not { } settings)
            return;
        _interaction.Begin(Scope);
        try
        {
            var result = await RequestAsync(feature, new GitRemoteOperations.ConfigureInput(settings.Repository.WorktreePath, name,
                settings.Revision, value, EditPushUrl), token).ConfigureAwait(true);
            if (result is { Code: "ok", Data: { } data })
            {
                RemoteSettings = data.Deserialize<GitRemoteOperations.RemoteList>(WireJson.Options);
                RemoteName = null;
                RemoteUrl = null;
            }
        }
        finally { _interaction.End(Scope); }
    }

    [RelayCommand(CanExecute = nameof(HasRemoteSelection))]
    private async Task FetchRemoteAsync(CancellationToken token)
    {
        if (SelectedRemote is not { } remote || RemoteSettings is not { } settings)
            return;
        _interaction.Begin(Scope);
        try
        {
            LastFetchStatus = null;
            var result = await RequestAsync(_git.Fetch, new GitRemoteOperations.FetchInput(settings.Repository.WorktreePath,
                remote.Name, settings.Revision), token).ConfigureAwait(true);
            if (result is { Code: "ok", Data: { } data })
                LastFetchStatus = data.Deserialize<GitRemoteOperations.FetchResult>(WireJson.Options)?.Status;
        }
        finally { _interaction.End(Scope); }
    }

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private async Task SetUpstreamAsync(CancellationToken token)
    {
        if (SelectedReference is not { } branch || PromptUpstreamAsync is not { } prompt || !CanSwitchBranch())
            return;
        var path = Page.Repository.WorktreePath;
        var name = branch.Name["refs/heads/".Length..];
        var candidates = References.Refs.Where(reference => string.Equals(reference.ObjectType, "commit", StringComparison.Ordinal) && reference.SymbolicTarget is null &&
            !string.Equals(reference.Name, branch.Name, StringComparison.Ordinal) &&
            (reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal) ||
             reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal))).ToArray();
        if (await prompt(name, candidates, token).ConfigureAwait(true) is { } choice && !token.IsCancellationRequested)
            await ChangeBranchAsync(_git.SetUpstream, new GitRemoteOperations.UpstreamInput(path, name, branch.ObjectId, choice.Upstream), token)
                .ConfigureAwait(true);
    }
}
