using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerFeatures
{
    internal sealed record ReferencesInput(DocumentContext Document, int Offset, bool IncludeDeclaration = true);
    internal sealed record OpenLocationInput(DocumentContext Document, Guid SessionId, LanguageLocation Target);

    internal Feature<CompleteInput> Hover { get; } = new("language.document.hover", "language_hover",
        "Read language-server hover information at the UTF-16 editor offset.",
        static (input, view, token) => QueryAsync(input.Document, new("textDocument/hover", "hoverProvider", input.Offset), view, token),
        typeof(LanguageQueryResult));

    internal Feature<CompleteInput> SignatureHelp { get; } = new("language.document.signature-help", "language_signature_help",
        "Read supported call signatures at the UTF-16 editor offset.",
        static (input, view, token) => QueryAsync(input.Document, new("textDocument/signatureHelp", "signatureHelpProvider", input.Offset), view, token),
        typeof(LanguageQueryResult));

    internal Feature<CompleteInput> Definition { get; } = LocationFeature("definition", "definition", "definition", "Find definitions at the editor offset.");
    internal Feature<CompleteInput> Declaration { get; } = LocationFeature("declaration", "declaration", "declaration", "Find declarations at the editor offset.");
    internal Feature<CompleteInput> TypeDefinition { get; } = LocationFeature("type-definition", "type_definition", "typeDefinition", "Find type definitions at the editor offset.");
    internal Feature<CompleteInput> Implementation { get; } = LocationFeature("implementation", "implementation", "implementation", "Find implementations at the editor offset.");

    internal Feature<ReferencesInput> References { get; } = new("language.document.references", "language_references",
        "Find references at the UTF-16 editor offset.",
        static (input, view, token) => QueryAsync(input.Document, new("textDocument/references", "referencesProvider",
            Parameters: position => new
            {
                textDocument = position.Document, position = position.Position(input.Offset),
                context = new { includeDeclaration = input.IncludeDeclaration },
            }), view, token, ReadLocationsAsync), typeof(LanguageNavigationResult));

    internal Feature<OpenLocationInput> OpenLocation { get; } = new("language.document.location.open", "language_open_location",
        "Open a language location in the workspace and move the editor to the target position.",
        OpenLocationAsync, typeof(LanguageOpenedLocation), mutation: true);

    private static Feature<CompleteInput> LocationFeature(string id, string name, string method, string description) =>
        new("language.document." + id, "language_" + name, description,
            (input, view, token) => QueryAsync(input.Document, new("textDocument/" + method, method + "Provider", input.Offset),
                view, token, ReadLocationsAsync), typeof(LanguageNavigationResult));

    private static Task<FeatureOutcome> ReadLocationsAsync(LanguageQueryResult result, ILanguageRegistration _, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(FeatureOutcome.Success("Language locations read.",
            new LanguageNavigationResult(result.Document, result.Server, LanguageLocation.Read(result.Result))));
    }

    private static async Task<FeatureOutcome> OpenLocationAsync(OpenLocationInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not ILanguageRegistration { HasExplorer: true, CanChangeExplorer: true } registration ||
            registration.ReadLanguageDocument(input.Document) is not { } origin ||
            registration.LanguageServers.ReadSession(origin, input.SessionId) is not { PositionEncoding: { } encoding })
            return DocumentChanged();

        if (input.Target.Range is not { IsOrdered: true } range ||
            LanguageTargetFile.Resolve(registration.Explorer, input.Target.Uri, input.Document.RootId) is not { } address)
            return new("invalid_language_location", "Choose a valid file location inside an open workspace root.");

        var target = await LanguageTargetFile.ReadAsync(registration.LanguageServers, address, token).ConfigureAwait(false);
        if (target is null)
            return new("source_unavailable", "The language location has no readable source document.");

        int offset;
        try { offset = range.Start.ToOffset(target.Text, encoding, clampColumn: true); }
        catch (ArgumentOutOfRangeException) { return new("invalid_language_location", "The location is outside the available source text."); }
        var point = LspPosition.AtOffset(target.Text, offset, "utf-16");
        var position = new SourcePosition(point.Line + 1, point.Character + 1);
        return new FeatureOutcome("ok", "Language location prepared.")
        {
            Commit = current => current is ILanguageRegistration { HasExplorer: true, CanChangeExplorer: true } active &&
                active.ReadLanguageDocument(input.Document) is { } live &&
                active.LanguageServers.ReadSession(live, input.SessionId) is not null && target.IsCurrent(active)
                    ? OpenTarget(active, target, position, offset) : DocumentChanged(),
        };
    }

    private static FeatureOutcome OpenTarget(ILanguageRegistration active, LanguageTargetFile target, SourcePosition position, int offset) =>
        active.OpenOrActivateDocument(target.Address.Entry, target.Source, position) is { } document
            ? FeatureOutcome.Success("Language location opened.", new LanguageOpenedLocation(document, offset, position))
            : new("editor_unavailable", "The destination editor could not be opened.");
}
