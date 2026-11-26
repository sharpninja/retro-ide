namespace RetroIde.Application.Workspaces;

internal interface IExplorerRegistration
{
    WorkspaceExplorer Explorer { get; }
    bool HasExplorer { get; }
    bool CanChangeExplorer { get; }
    bool HasOpenDocument(WorkspaceExplorer.Address address);
    DocumentContext? OpenOrActivateDocument(WorkspaceExplorer.Address address, WorkspaceExplorer.SourceFile? source, SourcePosition? position);
    DocumentContext? ReadDocumentContext(Guid viewKey);
    FeatureOutcome FindInDocument(DocumentFeatures.FindInput input);
    FeatureOutcome ReplaceInDocument(DocumentFeatures.ReplaceInput input);
    FeatureOutcome CloseDocument(DocumentFeatures.CloseInput input);
    FeatureOutcome EditDocument(DocumentFeatures.EditInput input);
    FeatureOutcome ApplyDocumentHistory(DocumentContext document, bool redo);
    DocumentSaveSnapshot? CaptureDocumentSave(DocumentContext context);
    DocumentContext? CommitDocumentSave(DocumentSaveSnapshot snapshot, PreparedDocumentSave staged);
    void PublishExplorer();
}
