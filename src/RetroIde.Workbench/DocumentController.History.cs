using Avalonia.Threading;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    private readonly Dictionary<string, ShellDocument> _histories = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal GitHistoryOperations.HistoryTab? ShowHistory(GitHistoryOperations.HistoryPage page, GitHistoryOperations.RefState references)
    {
        Dispatcher.UIThread.VerifyAccess();
        var path = page.Repository.WorktreePath;
        if (!_histories.TryGetValue(path, out var document))
        {
            var view = new GitHistoryViewModel(_workbench, _features.Git, _interaction, page, references);
            if (!AddOrUpdateView(view))
                return null;
            view.CommandRequested += OnHistoryCommandRequested;
            document = new ShellDocument(view.Scope, view.Title, "", _interaction)
            {
                GitHistory = view, CanClose = true, CanFloat = true,
            };
            _histories.Add(path, document);
            _layout.AddTransientDocument(document);
        }
        var active = document.GitHistory!;
        active.Display(page, references);
        _layout.ActivateDocument(document);
        return active.Capture();
    }

    internal bool CloseHistory(Guid viewKey)
    {
        if (GetView<IView>(viewKey) is not GitHistoryViewModel view || !_histories.TryGetValue(view.Page.Repository.WorktreePath, out var document))
            return false;

        var previous = _closingView;
        _closingView = viewKey;
        try { _layout.Factory.CloseDockable(document); }
        finally { _closingView = previous; }
        return !_histories.ContainsKey(view.Page.Repository.WorktreePath);
    }

    private void RemoveHistory(ShellDocument document)
    {
        if (document.GitHistory is not { } view || !_histories.Remove(view.Page.Repository.WorktreePath))
            return;
        view.CommandRequested -= OnHistoryCommandRequested;
        RemoveView(view);
        _layout.ForgetTransientDocument(document);
    }

    private void OnHistoryCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || sender is not GitHistoryViewModel view || request.OriginViewKey != view.ViewKey ||
            !ReferenceEquals(GetView<IView>(view.ViewKey), view))
            return;

        request.Completion = request.Payload switch
        {
            GitHistoryOperations.CloseHistoryInput close when close.ViewKey == view.ViewKey &&
                string.Equals(request.CommandId, _features.Git.CloseHistory.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.CloseHistory, close, request),
            GitHistoryOperations.HistoryInput page when string.Equals(request.CommandId, _features.Git.OpenHistory.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.OpenHistory, page, request),
            GitHistoryOperations.DetailsInput details when string.Equals(request.CommandId, _features.Git.CommitDetails.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.CommitDetails, details, request),
            GitBranchOperations.CreateInput create when string.Equals(request.CommandId, _features.Git.CreateBranch.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.CreateBranch, create, request),
            GitBranchOperations.SwitchInput branch when string.Equals(request.CommandId, _features.Git.SwitchBranch.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.SwitchBranch, branch, request),
            GitBranchOperations.RenameInput rename when string.Equals(request.CommandId, _features.Git.RenameBranch.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.RenameBranch, rename, request),
            GitBranchOperations.DeleteInput delete when string.Equals(request.CommandId, _features.Git.DeleteBranch.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.DeleteBranch, delete, request),
            GitRemoteOperations.ListInput remotes when string.Equals(request.CommandId, _features.Git.Remotes.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Remotes, remotes, request),
            GitHistoryOperations.RefsInput refs when string.Equals(request.CommandId, _features.Git.Refs.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Refs, refs, request),
            GitRemoteOperations.ConfigureInput add when string.Equals(request.CommandId, _features.Git.AddRemote.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.AddRemote, add, request),
            GitRemoteOperations.ConfigureInput url when string.Equals(request.CommandId, _features.Git.SetRemoteUrl.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.SetRemoteUrl, url, request),
            GitRemoteOperations.ConfigureInput renameRemote when string.Equals(request.CommandId, _features.Git.RenameRemote.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.RenameRemote, renameRemote, request),
            GitRemoteOperations.ConfigureInput remove when string.Equals(request.CommandId, _features.Git.RemoveRemote.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.RemoveRemote, remove, request),
            GitRemoteOperations.FetchInput fetch when string.Equals(request.CommandId, _features.Git.Fetch.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Fetch, fetch, request),
            GitRemoteOperations.PushInput push when string.Equals(request.CommandId, _features.Git.Push.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Push, push, request),
            GitRemoteOperations.PullInput pull when string.Equals(request.CommandId, _features.Git.Pull.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Pull, pull, request),
            GitOperationStart.Input start when string.Equals(request.CommandId, _features.Git.StartOperation.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.StartOperation, start, request),
            GitReset.ReviewInput review when string.Equals(request.CommandId, _features.Git.ReviewReset.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.ReviewReset, review, request),
            GitReset.ResetInput reset when string.Equals(request.CommandId, _features.Git.Reset.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.Reset, reset, request),
            GitRemoteOperations.UpstreamInput upstream when string.Equals(request.CommandId, _features.Git.SetUpstream.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.SetUpstream, upstream, request),
            _ => null,
        };
    }
}
