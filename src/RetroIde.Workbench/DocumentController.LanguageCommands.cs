using RetroIde.Application;
using RetroIde.Application.LanguageServers;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    private void DispatchLanguageCommand(DocumentViewModel view, ViewCommandRequested request)
    {
        var language = _features.Language;
        _ = request.Payload switch
        {
            LanguageServerFeatures.CompleteInput position when position.Document.ViewKey == view.ViewKey =>
                RouteLanguageQuery(view, request, position),
            LanguageServerFeatures.DocumentInput document when document.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, document, language.Symbols) ||
                RouteLanguageFeature(view, request, document, language.Diagnostics),
            LanguageServerFeatures.ReferencesInput references when references.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, references, language.References),
            LanguageServerFeatures.OpenLocationInput location when location.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, location, language.OpenLocation),
            LanguageServerFeatures.FormatInput format when format.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, format, language.Format),
            LanguageServerFeatures.RenameInput rename when rename.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, rename, language.Rename),
            LanguageServerFeatures.CodeActionsInput actions when actions.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, actions, language.CodeActions),
            LanguageServerFeatures.PreviewCompletionInput completion when completion.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, completion, language.PreviewCompletion),
            LanguageServerFeatures.PreviewActionInput action when action.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, action, language.PreviewAction),
            LanguageServerFeatures.ApplyEditInput edit when edit.Document.ViewKey == view.ViewKey =>
                RouteLanguageFeature(view, request, edit, language.ApplyEdit),
            _ => false,
        };
    }

    private bool RouteLanguageQuery(DocumentViewModel view, ViewCommandRequested request, LanguageServerFeatures.CompleteInput input)
    {
        var language = _features.Language;
        return RouteLanguageFeature(view, request, input, language.Complete) ||
            RouteLanguageFeature(view, request, input, language.Hover) ||
            RouteLanguageFeature(view, request, input, language.SignatureHelp) ||
            RouteLanguageFeature(view, request, input, language.Definition) ||
            RouteLanguageFeature(view, request, input, language.Declaration) ||
            RouteLanguageFeature(view, request, input, language.TypeDefinition) ||
            RouteLanguageFeature(view, request, input, language.Implementation);
    }

    private bool RouteLanguageFeature<T>(DocumentViewModel view, ViewCommandRequested request, T input, Feature<T> feature)
        where T : class
    {
        if (!string.Equals(request.CommandId, feature.Id, StringComparison.Ordinal))
            return false;

        request.Completion = CompleteAsync(view, feature, input, request);
        return true;
    }
}
