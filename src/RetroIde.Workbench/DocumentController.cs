using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core.Events;
using GPS.SimpleMVC.Controllers;
using RetroIde.Application;
using RetroIde.Application.Git;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController : SimpleControllerBase
{
    private readonly WorkbenchViewModel _workbench;
    private readonly DockLayout _layout;
    private readonly InteractionBlocks _interaction;
    private readonly FeatureRegistry _features;
    private Guid? _closingView;
    private readonly WorkbenchController _dispatch;
    private readonly Dictionary<WorkspaceExplorer.Address, ShellDocument> _open = new(WorkspaceExplorer.AddressEquality);
    private readonly Dictionary<(string Worktree, string Path, string Layer), ShellDocument> _diffs = [];

    public DocumentController(WorkbenchViewModel workbench, DockLayout layout, InteractionBlocks interaction,
        FeatureRegistry features, WorkbenchController dispatch)
    {
        _workbench = workbench;
        _layout = layout;
        _interaction = interaction;
        _features = features;
        _dispatch = dispatch;
        _layout.Factory.DockableClosed += OnTabClosed;
        _layout.Factory.DockableClosing += OnTabClosing;
        (_workbench.Localization ?? WorkbenchLocalization.English).Changed += OnDocumentLanguageChanged;
        Initialize();
    }

    public override bool Initialize() => true;

    internal bool HasDocument(WorkspaceExplorer.Address address) => _open.ContainsKey(address);

    internal bool Owns(Guid viewKey, Guid parentKey) =>
        parentKey == _workbench.ViewKey && GetView<IView>(viewKey) is DocumentViewModel or GitDiffViewModel or GitHistoryViewModel or GitBlameViewModel or TerminalViewModel;

    internal DocumentContext? OpenOrActivate(WorkspaceExplorer.Address address, WorkspaceExplorer.SourceFile? source, SourcePosition? position)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (source is not null && IsSwitchingPath(Path.GetFullPath(address.RelativePath, source.Address.RootPath)))
            return null;
        if (!_open.TryGetValue(address, out var document))
        {
            if (source is null)
                return null;
            var view = new DocumentViewModel(_workbench, source, _features);
            if (!AddOrUpdateView(view))
                return null;

            document = new ShellDocument($"source-{view.DocumentId:N}", Path.GetFileName(address.RelativePath), "", _interaction)
            {
                Source = view,
                CanClose = true,
                CanFloat = true,
            };
            _open.Add(address, document);
            view.CommandRequested += OnCommandRequested;
            view.PropertyChanged += OnDocumentStateChanged;
            view.LanguageContentChanged += OnLanguageContentChanged;
            _dispatch.LanguageServers.Observe(view.CaptureLanguageDocument());
            _layout.AddTransientDocument(document);
        }

        var active = document.Source!;
        if (IsSwitchingPath(active.FullPath))
            return null;
        if (position is not null)
            active.GoTo(position);
        _layout.ActivateDocument(document);
        return active.Capture();
    }

    internal DocumentContext? ReadContext(Guid key) => GetView<IView>(key) is DocumentViewModel view ? view.Capture() : null;

    internal LanguageDocument? ReadLanguageDocument(DocumentContext context) =>
        TryResolve(context, out var view) ? view.CaptureLanguageDocument() : null;

    private void OnLanguageContentChanged(object? sender, EventArgs args)
    {
        if (sender is DocumentViewModel view)
            _dispatch.LanguageServers.Observe(view.CaptureLanguageDocument());
    }

    internal GitFileOperations.DiffTab? ShowDiff(GitFileOperations.DiffResult diff)
    {
        Dispatcher.UIThread.VerifyAccess();
        var key = (diff.Repository.WorktreePath, diff.Path, diff.Layer);
        if (!_diffs.TryGetValue(key, out var document))
        {
            var view = new GitDiffViewModel(_workbench, _features.Git, _interaction, diff);
            if (!AddOrUpdateView(view))
                return null;
            view.CommandRequested += OnDiffCommandRequested;
            document = new ShellDocument(view.Scope, view.Title, "", _interaction)
            {
                GitDiff = view,
                CanClose = true,
                CanFloat = true,
            };
            _diffs.Add(key, document);
            _layout.AddTransientDocument(document);
        }
        var active = document.GitDiff!;
        active.DisplayPatch(diff);
        _layout.ActivateDocument(document);
        return active.Capture();
    }

    internal bool CloseDiff(Guid viewKey)
    {
        if (GetView<IView>(viewKey) is not GitDiffViewModel view || !_diffs.TryGetValue(view.Identity, out var document))
            return false;
        var previous = _closingView;
        _closingView = viewKey;
        try { _layout.Factory.CloseDockable(document); }
        finally { _closingView = previous; }
        return !_diffs.ContainsKey(view.Identity);
    }

    internal FeatureOutcome Find(DocumentFeatures.FindInput input)
    {
        if (!TryResolve(input.Document, out var view))
            return Changed();
        var position = view.EditorPosition;
        var next = input.Backward ? position.SelectionStart : position.SelectionStart + position.SelectionLength;
        var offset = DocumentSearch.Find(view.Buffer.Text, input.Query, Math.Clamp(next, 0, view.Buffer.TextLength), input.MatchCase, input.Backward);
        if (offset >= 0)
            view.ShowRange(offset, input.Query.Length);
        return SearchResult(view, offset, input.Query.Length, 0);
    }

    internal FeatureOutcome Replace(DocumentFeatures.ReplaceInput input)
    {
        if (!TryResolve(input.Document, out var view))
            return Changed();
        var text = view.Buffer.Text;
        var matches = input.All
            ? DocumentSearch.FindAll(text, input.Query, input.MatchCase)
            : FindReplacement(text, input, view.EditorPosition);
        if (matches.Count == 0)
            return SearchResult(view, -1, 0, 0);

        using (view.Buffer.RunUpdate())
            for (var i = matches.Count - 1; i >= 0; i--)
                view.Buffer.Replace(matches[i], input.Query.Length, input.Replacement);
        view.ShowRange(matches[0], input.Replacement.Length);
        return SearchResult(view, matches[0], input.Replacement.Length, matches.Count);
    }

    internal FeatureOutcome Edit(DocumentFeatures.EditInput input)
    {
        if (!TryResolve(input.Document, out var view))
            return Changed();
        if (input.Offset < 0 || input.Length < 0 || input.Offset > view.Buffer.TextLength - input.Length)
            return new("invalid_range", "The edit range must be inside the document.");

        view.Buffer.Replace(input.Offset, input.Length, input.Text);
        view.ShowRange(input.Offset + input.Text.Length, 0);
        return FeatureOutcome.Success("Document edited.", view.Capture());
    }

    internal FeatureOutcome ApplyHistory(DocumentContext document, bool redo)
    {
        if (!TryResolve(document, out var view))
            return Changed();

        var history = view.Buffer.UndoStack;
        if (redo ? !history.CanRedo : !history.CanUndo)
            return new("history_empty", redo ? "There is nothing to redo." : "There is nothing to undo.");
        if (redo)
            history.Redo();
        else
            history.Undo();
        return FeatureOutcome.Success(redo ? "Document edit redone." : "Document edit undone.", view.Capture());
    }

    internal FeatureOutcome Close(DocumentFeatures.CloseInput input)
    {
        if (!TryResolve(input.Document, out var view) || !_open.TryGetValue(view.Address, out var dockable))
            return Changed();
        if (view.IsDirty && !input.DiscardChanges)
            return new("unsaved", "Save or discard the changes before closing this document.");

        var previous = _closingView;
        _closingView = view.ViewKey;
        try { _layout.Factory.CloseDockable(dockable); }
        finally { _closingView = previous; }

        return _open.ContainsKey(view.Address)
            ? new("close_failed", "The source tab could not be closed.")
            : new("ok", "Source tab closed.");
    }

    private bool TryResolve(DocumentContext context, out DocumentViewModel view)
    {
        view = (GetView<IView>(context.ViewKey) as DocumentViewModel)!;
        return view is not null && view.Capture() == context && !_interaction.IsBlocked(view.Scope);
    }

    private static IReadOnlyList<int> FindReplacement(string text, DocumentFeatures.ReplaceInput input,
        (int Caret, int SelectionStart, int SelectionLength) position)
    {
        var start = Math.Clamp(position.SelectionStart, 0, text.Length);
        if (position.SelectionLength == input.Query.Length && position.SelectionLength <= text.Length - start &&
            text.AsSpan(start, position.SelectionLength).Equals(input.Query.AsSpan(), DocumentSearch.Comparison(input.MatchCase)))
            return [start];

        var next = DocumentSearch.Find(text, input.Query, start, input.MatchCase, backward: false);
        return next >= 0 ? [next] : [];
    }

    private static FeatureOutcome Changed() => new("document_changed", "The document is unavailable or its revision has changed.");

    private static FeatureOutcome SearchResult(DocumentViewModel view, int offset, int length, int replacements) =>
        FeatureOutcome.Success(offset < 0 ? "No matching text." : replacements == 0 ? "Match selected." : "Matching text replaced.",
            new DocumentFeatures.SearchResult(view.Capture(), offset >= 0 ? offset : null, offset >= 0 ? length : 0, replacements));

    internal DocumentSaveSnapshot? CaptureSave(DocumentContext context)
    {
        return TryResolve(context, out var view) ? view.CaptureSave(context) : null;
    }

    internal DocumentContext? CommitSave(DocumentSaveSnapshot snapshot, PreparedDocumentSave staged)
    {
        if (GetView<IView>(snapshot.Context.ViewKey) is not DocumentViewModel view || view.DocumentId != snapshot.Context.DocumentId ||
            _interaction.IsBlocked(view.Scope))
            return null;
        if (snapshot.Context.Revision < view.SavedRevision)
            throw new IOException("A newer document revision was already saved.");

        staged.Commit();
        view.MarkSaved(snapshot);
        return view.Capture();
    }

    internal bool HasUnsavedDocuments => _open.Values.Any(document => document.Source?.IsDirty == true);

    internal void DetachViews()
    {
        foreach (var document in _open.Values.ToArray())
            RemoveDocument(document);
        foreach (var document in _diffs.Values.ToArray())
            RemoveDiff(document);
        foreach (var document in _histories.Values.ToArray())
            RemoveHistory(document);
        foreach (var document in _blames.Values.ToArray())
            RemoveBlame(document);
        foreach (var document in _terminals.Values.ToArray())
            RemoveTerminal(document);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _layout.Factory.DockableClosed -= OnTabClosed;
            _layout.Factory.DockableClosing -= OnTabClosing;
            (_workbench.Localization ?? WorkbenchLocalization.English).Changed -= OnDocumentLanguageChanged;
            DetachViews();
        }
        base.Dispose(disposing);
    }

    private void OnTabClosing(object? sender, DockableClosingEventArgs args)
    {
        if (args.Dockable is not ShellDocument document)
            return;
        IView? view = (IView?)document.Source ?? (IView?)document.GitDiff ?? (IView?)document.GitHistory ??
            (IView?)document.GitBlame ?? document.GitTerminal;
        IAsyncRelayCommand? close = document.Source?.CloseCommand ?? document.GitDiff?.CloseCommand ??
            document.GitHistory?.CloseCommand ?? document.GitBlame?.CloseCommand ?? document.GitTerminal?.CloseCommand;
        if (view is not null && close is not null && _closingView != view.ViewKey &&
            ReferenceEquals(GetView<IView>(view.ViewKey), view))
        {
            args.Cancel = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(GetView<IView>(view.ViewKey), view))
                    close.Execute(null);
            }, DispatcherPriority.Background);
        }
    }

    private void OnDocumentStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is DocumentViewModel view && string.Equals(args.PropertyName, nameof(DocumentViewModel.IsDirty), StringComparison.Ordinal) &&
            _open.TryGetValue(view.Address, out var document))
            document.Title = Path.GetFileName(view.Address.RelativePath) + (view.IsDirty ? " *" : "");
    }

    private void OnTabClosed(object? sender, DockableClosedEventArgs args)
    {
        if (args.Dockable is ShellDocument { IsTransient: true } document)
        {
            if (document.GitHistory is not null)
                RemoveHistory(document);
            else if (document.GitDiff is not null)
                RemoveDiff(document);
            else if (document.GitBlame is not null)
                RemoveBlame(document);
            else if (document.GitTerminal is not null)
                RemoveTerminal(document);
            else
                RemoveDocument(document);
            _layout.AdvanceWorkspaceRevision();
        }
    }

    private void OnDocumentLanguageChanged(object? sender, EventArgs args)
    {
        foreach (var document in _diffs.Values)
            document.Title = document.GitDiff!.Title;
        foreach (var document in _histories.Values)
            document.Title = document.GitHistory!.Title;
        foreach (var document in _blames.Values)
            document.Title = document.GitBlame!.Title;
        foreach (var document in _terminals.Values)
            document.Title = document.GitTerminal!.Title;
    }

    private void RemoveDiff(ShellDocument document)
    {
        if (document.GitDiff is not { } view || !_diffs.Remove(view.Identity))
            return;
        view.CommandRequested -= OnDiffCommandRequested;
        RemoveView(view);
        _layout.ForgetTransientDocument(document);
    }

    private void RemoveDocument(ShellDocument document)
    {
        if (document.Source is not { } view || !_open.Remove(view.Address))
            return;
        view.CommandRequested -= OnCommandRequested;
        view.PropertyChanged -= OnDocumentStateChanged;
        view.LanguageContentChanged -= OnLanguageContentChanged;
        _dispatch.LanguageServers.Closed(view.CaptureLanguageDocument());
        RemoveView(view);
        _layout.ForgetTransientDocument(document);
    }

    private void OnCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || sender is not DocumentViewModel view || request.OriginViewKey != view.ViewKey ||
            !ReferenceEquals(GetView<IView>(view.ViewKey), view))
            return;

        request.Completion = request.Payload switch
        {
            ExplorerFeatures.RevealEntryInput reveal when string.Equals(request.CommandId, _features.Explorer.RevealEntry.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Explorer.RevealEntry, reveal, request),
            ExplorerFeatures.SaveDocumentInput save when string.Equals(request.CommandId, _features.Explorer.SaveDocument.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Explorer.SaveDocument, save, request),
            DocumentFeatures.FindInput find when string.Equals(request.CommandId, _features.Documents.Find.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Documents.Find, find, request),
            DocumentFeatures.ReplaceInput replace when string.Equals(request.CommandId, _features.Documents.Replace.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Documents.Replace, replace, request),
            DocumentFeatures.HistoryInput undo when string.Equals(request.CommandId, _features.Documents.Undo.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Documents.Undo, undo, request),
            DocumentFeatures.HistoryInput redo when string.Equals(request.CommandId, _features.Documents.Redo.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Documents.Redo, redo, request),
            DocumentFeatures.CloseInput close when string.Equals(request.CommandId, _features.Documents.Close.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Documents.Close, close, request),
            _ => null,
        };
    }

    private void OnDiffCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || sender is not GitDiffViewModel view || request.OriginViewKey != view.ViewKey ||
            !ReferenceEquals(GetView<IView>(view.ViewKey), view))
            return;

        request.Completion = request.Payload switch
        {
            GitFileOperations.CloseDiffInput close when close.ViewKey == view.ViewKey &&
                string.Equals(request.CommandId, _features.Git.CloseDiff.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.CloseDiff, close, request),
            GitFileOperations.DiffInput refresh when string.Equals(request.CommandId, _features.Git.OpenDiff.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.OpenDiff, refresh, request),
            GitFileOperations.HunksInput stage when string.Equals(request.CommandId, _features.Git.StageHunks.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.StageHunks, stage, request),
            GitFileOperations.HunksInput unstage when string.Equals(request.CommandId, _features.Git.UnstageHunks.Id, StringComparison.Ordinal) =>
                CompleteAsync(view, _features.Git.UnstageHunks, unstage, request),
            _ => null,
        };
    }

    private async Task CompleteAsync<TInput>(IView origin, Feature<TInput> feature, TInput input,
        ViewCommandRequested request) where TInput : class
    {
        var result = await _dispatch.DispatchDocumentAsync(feature, input, request).ConfigureAwait(false);
        await _dispatch.DeliverResultAsync(origin, request, result,
            () => ReferenceEquals(GetView<IView>(origin.ViewKey), origin)).ConfigureAwait(false);
    }
}
