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
    [NotifyCanExecuteChangedFor(nameof(FetchLfsCommand), nameof(CheckoutLfsCommand), nameof(PullLfsCommand))]
    public partial GitLfsState.Status? LfsStatus { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchLfsCommand), nameof(PullLfsCommand))]
    public partial string? LfsRemote { get; set; }

    internal Func<CancellationToken, Task>? PromptLfsAsync { get; set; }
    internal Control WrapLfs(Control content) => Interaction.Wrap(PaneId, content);

    private bool CanCheckoutLfs() => CanRefreshStatus() && LfsStatus is { IsInstalled: true, Head: not null, Files.Length: > 0 };
    private bool CanTransferLfs() => CanCheckoutLfs() && LfsRemote is not null;

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task OpenLfsAsync(CancellationToken token)
    {
        SetLfs(null);
        await RefreshLfsAsync(token).ConfigureAwait(true);
        if (LfsStatus is not null && PromptLfsAsync is { } prompt && !token.IsCancellationRequested)
            await prompt(token).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task RefreshLfsAsync(CancellationToken token)
    {
        if (Repository is not { } repo)
            return;
        var result = await RequestAsync(Git.LfsStatus, new GitLfsOperations.StatusInput(repo.WorktreePath), token).ConfigureAwait(true);
        ApplyLfsResult(repo, result);
    }

    [RelayCommand(CanExecute = nameof(CanTransferLfs))]
    private Task FetchLfsAsync(CancellationToken token) => RunLfsAsync(Git.FetchLfs, LfsRemote, token);

    [RelayCommand(CanExecute = nameof(CanCheckoutLfs))]
    private Task CheckoutLfsAsync(CancellationToken token) => RunLfsAsync(Git.CheckoutLfs, remote: null, token);

    [RelayCommand(CanExecute = nameof(CanTransferLfs))]
    private Task PullLfsAsync(CancellationToken token) => RunLfsAsync(Git.PullLfs, LfsRemote, token);

    private async Task RunLfsAsync(Feature<GitLfsOperations.ActionInput> feature, string? remote, CancellationToken token)
    {
        if (Repository is not { } repo || LfsStatus is not { IsInstalled: true } review)
            return;
        Interaction.Begin(PaneId);
        try
        {
            var input = new GitLfsOperations.ActionInput(repo.WorktreePath, review.Revision, remote);
            var result = await RequestAsync(feature, input, token).ConfigureAwait(true);
            ApplyLfsResult(repo, result);
        }
        finally { Interaction.End(PaneId); }
    }

    private void ApplyLfsResult(GitWorktree repo, OperationResult? result)
    {
        if (ReferenceEquals(Repository, repo) && result is { Code: "ok", Data: { } data } &&
            data.Deserialize<GitLfsState.Status>(WireJson.Options) is { } status)
            SetLfs(status);
    }

    private void SetLfs(GitLfsState.Status? status)
    {
        var previous = LfsRemote;
        LfsStatus = status;
        LfsRemote = status is null ? null :
            previous is not null && status.Remotes.Contains(previous, StringComparer.Ordinal) ? previous :
            status.Remotes.Contains("origin", StringComparer.Ordinal) ? "origin" : status.Remotes.FirstOrDefault();
    }

    private void NotifyLfsCommands()
    {
        OpenLfsCommand.NotifyCanExecuteChanged();
        RefreshLfsCommand.NotifyCanExecuteChanged();
        FetchLfsCommand.NotifyCanExecuteChanged();
        CheckoutLfsCommand.NotifyCanExecuteChanged();
        PullLfsCommand.NotifyCanExecuteChanged();
    }
}
