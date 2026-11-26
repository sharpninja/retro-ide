using Avalonia.Threading;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    private readonly Dictionary<GitBlameOperations.Input, ShellDocument> _blames = [];

    internal GitBlameOperations.Tab? ShowBlame(GitBlameOperations.Blame blame)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_blames.TryGetValue(blame.Source, out var document))
        {
            var view = new GitBlameViewModel(_workbench, _features.Git, blame);
            if (!AddOrUpdateView(view))
                return null;
            view.CommandRequested += OnBlameCommandRequested;
            document = new ShellDocument(view.Scope, view.Title, "", _interaction)
            {
                GitBlame = view, CanClose = true, CanFloat = true,
            };
            _blames.Add(blame.Source, document);
            _layout.AddTransientDocument(document);
        }
        var active = document.GitBlame!;
        active.Display(blame);
        _layout.ActivateDocument(document);
        return active.Capture();
    }

    internal bool CloseBlame(Guid viewKey)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (GetView<IView>(viewKey) is not GitBlameViewModel view || !_blames.TryGetValue(view.Blame.Source, out var document))
            return false;

        var previous = _closingView;
        _closingView = viewKey;
        try { _layout.Factory.CloseDockable(document); }
        finally { _closingView = previous; }
        return !_blames.ContainsKey(view.Blame.Source);
    }

    private void RemoveBlame(ShellDocument document)
    {
        if (document.GitBlame is not { } view || !_blames.Remove(view.Blame.Source))
            return;
        view.CommandRequested -= OnBlameCommandRequested;
        RemoveView(view);
        _layout.ForgetTransientDocument(document);
    }

    private void OnBlameCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || sender is not GitBlameViewModel view || request.OriginViewKey != view.ViewKey ||
            !ReferenceEquals(GetView<IView>(view.ViewKey), view))
            return;

        request.Completion = request.Payload switch
        {
            GitBlameOperations.Input input when string.Equals(request.CommandId, _features.Git.OpenBlame.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.OpenBlame, input, request),
            GitBlameOperations.CloseInput close when close.ViewKey == view.ViewKey &&
                string.Equals(request.CommandId, _features.Git.CloseBlame.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.CloseBlame, close, request),
            _ => null,
        };
    }
}
