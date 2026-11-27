namespace RetroIde.Application.LanguageServers;

internal static class LspDocumentMethods
{
    internal const string Completion = "textDocument/completion";
    internal const string CompletionResolve = "completionItem/resolve";
    internal const string Hover = "textDocument/hover";
    internal const string SignatureHelp = "textDocument/signatureHelp";
    internal const string Definition = "textDocument/definition";
    internal const string Declaration = "textDocument/declaration";
    internal const string TypeDefinition = "textDocument/typeDefinition";
    internal const string Implementation = "textDocument/implementation";
    internal const string References = "textDocument/references";
    internal const string Symbols = "textDocument/documentSymbol";
    internal const string Diagnostics = "textDocument/publishDiagnostics";
    internal const string Formatting = "textDocument/formatting";
    internal const string RangeFormatting = "textDocument/rangeFormatting";
    internal const string Rename = "textDocument/rename";
    internal const string CodeAction = "textDocument/codeAction";
    internal const string CodeActionResolve = "codeAction/resolve";

    internal static IReadOnlyDictionary<string, string> Providers { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Completion] = "completionProvider",
        [Hover] = "hoverProvider",
        [SignatureHelp] = "signatureHelpProvider",
        [Definition] = "definitionProvider",
        [Declaration] = "declarationProvider",
        [TypeDefinition] = "typeDefinitionProvider",
        [Implementation] = "implementationProvider",
        [References] = "referencesProvider",
        [Symbols] = "documentSymbolProvider",
        [Formatting] = "documentFormattingProvider",
        [RangeFormatting] = "documentRangeFormattingProvider",
        [Rename] = "renameProvider",
        [CodeAction] = "codeActionProvider",
    };
}
