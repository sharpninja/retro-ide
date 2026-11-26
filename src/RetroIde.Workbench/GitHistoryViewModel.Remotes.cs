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
    [NotifyCanExecuteChangedFor(nameof(EditRemoteCommand), nameof(RenameRemoteCommand), nameof(RemoveRemoteCommand), nameof(FetchRemoteCommand),
        nameof(PushRemoteCommand), nameof(PullRemoteCommand))]
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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushRemoteCommand), nameof(PullRemoteCommand))]
    public partial string? SyncBranch { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushRemoteCommand), nameof(PullRemoteCommand))]
    public partial string? RemoteBranch { get; set; }
    [ObservableProperty]
    public partial string PullStrategy { get; set; } = "fast-forward";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushRemoteCommand))]
    public partial bool ForceWithLease { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushRemoteCommand))]
    public partial string? ExpectedRemoteHead { get; set; }

    [ObservableProperty]
    public partial string? SyncHead { get; private set; }

    internal Control WrapRemoteSurface(Control content) => _interaction.Wrap(Scope, content);

    private bool CanAddRemote() => !string.IsNullOrWhiteSpace(RemoteName) && !string.IsNullOrWhiteSpace(RemoteUrl);
    private bool CanEditRemote() => SelectedRemote is not null && !string.IsNullOrWhiteSpace(RemoteUrl);
    private bool CanRenameRemote() => SelectedRemote is not null && !string.IsNullOrWhiteSpace(RemoteName);
    private bool HasRemoteSelection() => SelectedRemote is not null;
    private bool CanSyncRemote() => HasRemoteSelection() && SyncBranch is not null && !string.IsNullOrWhiteSpace(RemoteBranch);
    private bool CanPushRemote() => CanSyncRemote() && (!ForceWithLease || GitHistoryOperations.IsObjectId(ExpectedRemoteHead?.Trim()));

    [RelayCommand]
    private async Task ShowRemotesAsync(CancellationToken token)
    {
        await RefreshRemotesAsync(token).ConfigureAwait(true);
        if (RemoteSettings is null || PromptRemotesAsync is not { } prompt || token.IsCancellationRequested)
            return;
        RemoteBranch = SyncBranch;
        PullStrategy = "fast-forward";
        ForceWithLease = false;
        ExpectedRemoteHead = null;
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
        if (RemoteSettings is null)
            return;
        var refs = await RequestAsync(_git.Refs, new GitHistoryOperations.RefsInput(Page.Repository.WorktreePath), token).ConfigureAwait(true);
        SetSyncBranch(refs is { Code: "ok", Data: { } snapshot }
            ? snapshot.Deserialize<GitHistoryOperations.RefState>(WireJson.Options)
            : null);
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

    [RelayCommand(CanExecute = nameof(CanPushRemote))]
    private Task PushRemoteAsync(CancellationToken token) => SyncRemoteAsync(pull: false, token);

    [RelayCommand(CanExecute = nameof(CanSyncRemote))]
    private Task PullRemoteAsync(CancellationToken token) => SyncRemoteAsync(pull: true, token);

    private async Task SyncRemoteAsync(bool pull, CancellationToken token)
    {
        if (SelectedRemote is not { } remote || RemoteSettings is not { } settings ||
            SyncBranch is not { } branch || SyncHead is not { } head || string.IsNullOrWhiteSpace(RemoteBranch))
            return;
        var remoteBranch = RemoteBranch.Trim();
        var path = settings.Repository.WorktreePath;
        _interaction.Begin(Scope);
        try
        {
            LastFetchStatus = null;
            var result = pull
                ? await RequestAsync(_git.Pull, new GitRemoteOperations.PullInput(path, remote.Name, settings.Revision, branch, head,
                    remoteBranch, PullStrategy), token).ConfigureAwait(true)
                : await RequestAsync(_git.Push, new GitRemoteOperations.PushInput(path, remote.Name, settings.Revision, branch, head,
                    remoteBranch, ForceWithLease, ForceWithLease ? ExpectedRemoteHead?.Trim() : null), token).ConfigureAwait(true);
            if (result is { Code: "ok", Data: { } data } && data.Deserialize<GitRemoteOperations.SyncResult>(WireJson.Options) is { } sync)
            {
                if (pull)
                    SyncHead = sync.Head;
                LastFetchStatus = sync.Status;
                ForceWithLease = false;
                ExpectedRemoteHead = null;
            }
        }
        finally { _interaction.End(Scope); }
    }

    private void SetSyncBranch(GitHistoryOperations.RefState? references)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var tree = references is null ? null : Array.Find(references.Worktrees,
            item => string.Equals(item.Path, Page.Repository.WorktreePath, comparison));
        const string Prefix = "refs/heads/";
        SyncBranch = tree?.Branch is { } name && name.StartsWith(Prefix, StringComparison.Ordinal) &&
            GitHistoryOperations.IsObjectId(tree.Head) ? name[Prefix.Length..] : null;
        SyncHead = tree?.Head;
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
