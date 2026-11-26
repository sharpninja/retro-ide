using System.Text.Json.Serialization;

namespace RetroIde.Application.Workspaces;

internal sealed class DocumentFeatures
{
    internal sealed record ContextInput(Guid ViewKey);
    internal sealed record FindInput(DocumentContext Document, string Query, bool MatchCase = false, bool Backward = false);
    internal sealed record ReplaceInput(DocumentContext Document, string Query, string Replacement, bool MatchCase = false, bool All = false);
    internal sealed record CloseInput(DocumentContext Document, bool DiscardChanges = false);
    internal sealed record EditInput(DocumentContext Document, int Offset, int Length, string Text);
    internal sealed record HistoryInput(DocumentContext Document);
    internal sealed record SearchResult(DocumentContext Document,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? Offset, int Length, int Replacements);

    public Feature<ContextInput> GetContext { get; } = new(
        "document.context.read", "editor_get_document", "Read the live public context of a source tab by its view key.", false,
        static (input, view) => view is IExplorerRegistration { HasExplorer: true } explorer &&
            explorer.ReadDocumentContext(input.ViewKey) is { } context
                ? FeatureOutcome.Success("Document context read.", context)
                : new("view_closed", "The source tab is no longer available."), typeof(DocumentContext));

    public Feature<FindInput> Find { get; } = new(
        "document.find", "editor_find_text", "Select the next or previous literal match in the specified document.", true,
        static (input, view) => string.IsNullOrEmpty(input.Query)
            ? new("invalid_query", "Enter text to find.")
            : view is IExplorerRegistration { HasExplorer: true } explorer ? explorer.FindInDocument(input) : Unavailable(),
        typeof(SearchResult));

    public Feature<ReplaceInput> Replace { get; } = new(
        "document.replace", "editor_replace_text", "Replace one or all literal matches in the specified document.", true,
        static (input, view) => string.IsNullOrEmpty(input.Query)
            ? new("invalid_query", "Enter text to find.")
            : view is IExplorerRegistration { HasExplorer: true } explorer ? explorer.ReplaceInDocument(input) : Unavailable(),
        typeof(SearchResult));

    public Feature<CloseInput> Close { get; } = new(
        "document.close", "editor_close_document", "Close a source tab at its current revision. Unsaved edits require explicit discard.", true,
        static (input, view) => view is IExplorerRegistration { HasExplorer: true } explorer
            ? explorer.CloseDocument(input)
            : Unavailable());

    public Feature<EditInput> Edit { get; } = new(
        "document.edit", "editor_edit_text", "Replace the specified text range at the document's current revision.", true,
        static (input, view) => view is IExplorerRegistration { HasExplorer: true } explorer
            ? explorer.EditDocument(input)
            : Unavailable(), typeof(DocumentContext));

    public Feature<HistoryInput> Undo { get; } = new(
        "document.undo", "editor_undo", "Undo the latest edit in the specified document.", true,
        static (input, view) => view is IExplorerRegistration { HasExplorer: true } explorer
            ? explorer.ApplyDocumentHistory(input.Document, redo: false)
            : Unavailable(), typeof(DocumentContext));

    public Feature<HistoryInput> Redo { get; } = new(
        "document.redo", "editor_redo", "Redo the previously undone edit in the specified document.", true,
        static (input, view) => view is IExplorerRegistration { HasExplorer: true } explorer
            ? explorer.ApplyDocumentHistory(input.Document, redo: true)
            : Unavailable(), typeof(DocumentContext));

    private static FeatureOutcome Unavailable() => new("view_closed", "The source tab is no longer available.");
}
