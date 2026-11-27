using System.Text.Json;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class DocumentViewModel : ObservableObject, IView
{
    private readonly WorkbenchViewModel _workbench;
    private WorkspaceExplorer.SourceFile _source;
    internal WorkbenchLocalization Localization => _workbench.Localization ?? WorkbenchLocalization.English;

    internal static ViewContextContract ContextContract { get; } = new(typeof(DocumentViewModel), typeof(DocumentContext));

    private readonly FeatureRegistry _features;
    private string _savedText;

    public DocumentViewModel(WorkbenchViewModel workbench, WorkspaceExplorer.SourceFile source, FeatureRegistry features)
    {
        _workbench = workbench;
        _source = source;
        _features = features;
        _savedText = source.Text;
        SearchText = "";
        Replacement = "";
        Buffer = new TextDocument(source.Text);
        Buffer.Changed += (_, _) =>
        {
            Revision++;
            ClearLanguagePresentation();
            IsDirty = !string.Equals(Buffer.Text, _savedText, StringComparison.Ordinal);
            LanguageContentChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    [McpContext]
    public Guid ViewKey { get; } = Guid.NewGuid();
    [McpContext]
    public Guid DocumentId { get; } = Guid.NewGuid();
    [McpContext]
    public Guid WorkspaceId => _workbench.WorkspaceId;
    public WorkspaceExplorer.Address Address => _source.Address.Entry;
    internal WorkspaceExplorer.SourceAddress SourceAddress => _source.Address;
    internal string FullPath => Path.GetFullPath(Address.RelativePath, _source.Address.RootPath);
    [McpContext]
    public Guid RootId => Address.RootId;
    [McpContext]
    public string RelativePath => Address.RelativePath;
    public TextDocument Buffer { get; }
    [McpContext]
    public long Revision { get; private set; }
    internal long SavedRevision { get; private set; }
    private (int Caret, int SelectionStart, int SelectionLength) _editorPosition;

    internal (int Caret, int SelectionStart, int SelectionLength) EditorPosition
    {
        get => _editorPosition;
        set
        {
            var formatScopeChanged = (_editorPosition.SelectionLength == 0) != (value.SelectionLength == 0);
            _editorPosition = value;
            if (formatScopeChanged)
                LanguageFormatCommand.NotifyCanExecuteChanged();
        }
    }
    internal string Scope => $"source-{DocumentId:N}";

    public event EventHandler<ViewCommandRequested>? CommandRequested;
    internal event EventHandler? LanguageContentChanged;
    internal event EventHandler? PositionRequested;
    internal event EventHandler? SearchRequested;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string Replacement { get; set; }

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscardAndCloseCommand))]
    public partial bool CanDiscard { get; private set; }

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    internal DocumentContext Capture() => ContextContract.Capture(this).Deserialize<DocumentContext>(WireJson.Options)!;

    internal void GoTo(SourcePosition position)
    {
        var line = Buffer.GetLineByNumber(Math.Clamp(position.Line, 1, Buffer.LineCount));
        ShowRange(line.Offset + Math.Clamp(position.Column - 1, 0, line.Length), 0);
    }

    internal void ShowRange(int offset, int length)
    {
        EditorPosition = (offset, offset, length);
        PositionRequested?.Invoke(this, EventArgs.Empty);
    }

    internal DocumentSaveSnapshot CaptureSave(DocumentContext context) => new(context, _source.Address, Buffer.Text, _source.Encoding);

    internal LanguageDocument CaptureLanguageDocument() => new(Capture(), FullPath, Buffer.Text, SavedRevision);

    internal void MarkSaved(DocumentSaveSnapshot snapshot)
    {
        SavedRevision = snapshot.Context.Revision;
        _savedText = snapshot.Text;
        IsDirty = !string.Equals(Buffer.Text, _savedText, StringComparison.Ordinal);
        LanguageContentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ReloadSource(WorkspaceExplorer.SourceFile source)
    {
        _source = source;
        _savedText = source.Text;
        if (!string.Equals(Buffer.Text, source.Text, StringComparison.Ordinal))
            Buffer.Text = source.Text;
        Buffer.UndoStack.ClearAll();
        SavedRevision = Revision;
        IsDirty = false;
        LanguageContentChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsDirtyChanged(bool value)
    {
        if (!value)
            CanDiscard = false;
    }

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
        CanDiscard = IsDirty && string.Equals(result.Code, "unsaved", StringComparison.Ordinal);
    }

    [RelayCommand]
    private void ShowSearch() => SearchRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private Task FindAsync(CancellationToken token) => SendAsync(_features.Documents.Find.Id,
        new DocumentFeatures.FindInput(Capture(), SearchText ?? "", MatchCase), token);

    [RelayCommand]
    private Task FindPreviousAsync(CancellationToken token) => SendAsync(_features.Documents.Find.Id,
        new DocumentFeatures.FindInput(Capture(), SearchText ?? "", MatchCase, Backward: true), token);

    [RelayCommand]
    private Task ReplaceAsync(CancellationToken token) => SendAsync(_features.Documents.Replace.Id,
        new DocumentFeatures.ReplaceInput(Capture(), SearchText ?? "", Replacement ?? "", MatchCase), token);

    [RelayCommand]
    private Task ReplaceAllAsync(CancellationToken token) => SendAsync(_features.Documents.Replace.Id,
        new DocumentFeatures.ReplaceInput(Capture(), SearchText ?? "", Replacement ?? "", MatchCase, All: true), token);

    [RelayCommand]
    private Task UndoAsync(CancellationToken token) => SendAsync(_features.Documents.Undo.Id,
        new DocumentFeatures.HistoryInput(Capture()), token);

    [RelayCommand]
    private Task RedoAsync(CancellationToken token) => SendAsync(_features.Documents.Redo.Id,
        new DocumentFeatures.HistoryInput(Capture()), token);

    [RelayCommand]
    private Task CloseAsync(CancellationToken token) => SendAsync(_features.Documents.Close.Id,
        new DocumentFeatures.CloseInput(Capture()), token);

    private bool ShouldDiscard() => CanDiscard;

    [RelayCommand(CanExecute = nameof(ShouldDiscard))]
    private Task DiscardAndCloseAsync(CancellationToken token) => SendAsync(_features.Documents.Close.Id,
        new DocumentFeatures.CloseInput(Capture(), DiscardChanges: true), token);

    [RelayCommand]
    private Task RevealAsync(CancellationToken token) =>
        SendAsync(_features.Explorer.RevealEntry.Id, new ExplorerFeatures.RevealEntryInput(Address.RootId, Address.RelativePath), token);

    [RelayCommand]
    private Task SaveAsync(CancellationToken token) =>
        SendAsync(_features.Explorer.SaveDocument.Id, new ExplorerFeatures.SaveDocumentInput(Capture()), token);

    private async Task<OperationResult?> SendAsync(string id, object input, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), id, _workbench.Address, ViewKey, input, token);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        else
        {
            request.Result = OperationResult.Failure(request.RequestId, "view_closed", "The source tab registration is no longer live.");
            Receive(request.Result);
        }
        return request.Result;
    }
}
