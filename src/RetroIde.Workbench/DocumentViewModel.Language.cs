using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class DocumentViewModel
{
    private long _languageRequest;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LanguageCompleteCommand), nameof(LanguageHoverCommand), nameof(LanguageSignatureHelpCommand),
        nameof(LanguageDefinitionCommand), nameof(LanguageDeclarationCommand), nameof(LanguageTypeDefinitionCommand),
        nameof(LanguageImplementationCommand), nameof(LanguageReferencesCommand), nameof(LanguageSymbolsCommand),
        nameof(LanguageDiagnosticsCommand), nameof(LanguageFormatCommand), nameof(LanguageRenameCommand), nameof(LanguageCodeActionsCommand))]
    public partial LanguageDocumentCapabilities LanguageCapabilities { get; private set; } = LanguageDocumentCapabilities.Unavailable;

    internal void UpdateLanguageCapabilities(LanguageDocumentCapabilities next)
    {
        var current = LanguageCapabilities;
        if (current.SessionId == next.SessionId && current.Methods.SequenceEqual(next.Methods, StringComparer.Ordinal))
            return;

        if (current.SessionId != next.SessionId || current.Methods.Any(method => !next.Supports(method)))
            ClearLanguagePresentation();
        LanguageCapabilities = next;
    }

    private bool CanComplete() => LanguageCapabilities.Supports(LspDocumentMethods.Completion);
    private bool CanHover() => LanguageCapabilities.Supports(LspDocumentMethods.Hover);
    private bool CanReadSignatures() => LanguageCapabilities.Supports(LspDocumentMethods.SignatureHelp);
    private bool CanFindDefinition() => LanguageCapabilities.Supports(LspDocumentMethods.Definition);
    private bool CanFindDeclaration() => LanguageCapabilities.Supports(LspDocumentMethods.Declaration);
    private bool CanFindTypeDefinition() => LanguageCapabilities.Supports(LspDocumentMethods.TypeDefinition);
    private bool CanFindImplementation() => LanguageCapabilities.Supports(LspDocumentMethods.Implementation);
    private bool CanFindReferences() => LanguageCapabilities.Supports(LspDocumentMethods.References);
    private bool CanReadSymbols() => LanguageCapabilities.Supports(LspDocumentMethods.Symbols);
    private bool CanReadDiagnostics() => LanguageCapabilities.Supports(LspDocumentMethods.Diagnostics);
    private bool CanFormat() => LanguageCapabilities.Supports(EditorPosition.SelectionLength == 0
        ? LspDocumentMethods.Formatting : LspDocumentMethods.RangeFormatting);
    private bool CanReadCodeActions() => LanguageCapabilities.Supports(LspDocumentMethods.CodeAction);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLanguageResultCommand))]
    public partial LanguageEditorRow? SelectedLanguageRow { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyLanguageEditCommand))]
    public partial LanguageEditorResult? LanguageResult { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LanguageRenameCommand))]
    public partial string? LanguageRenameText { get; set; }

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private Task LanguageCompleteAsync(CancellationToken token) => QueryAtCaretAsync(_features.Language.Complete, LanguageEditorResult.Completions, token);

    [RelayCommand(CanExecute = nameof(CanHover))]
    private Task LanguageHoverAsync(CancellationToken token) => QueryAtCaretAsync(_features.Language.Hover, LanguageEditorResult.Hover, token);

    [RelayCommand(CanExecute = nameof(CanReadSignatures))]
    private Task LanguageSignatureHelpAsync(CancellationToken token) =>
        QueryAtCaretAsync(_features.Language.SignatureHelp, LanguageEditorResult.Signatures, token);

    [RelayCommand(CanExecute = nameof(CanFindDefinition))]
    private Task LanguageDefinitionAsync(CancellationToken token) => NavigateAsync(_features.Language.Definition, token);

    [RelayCommand(CanExecute = nameof(CanFindDeclaration))]
    private Task LanguageDeclarationAsync(CancellationToken token) => NavigateAsync(_features.Language.Declaration, token);

    [RelayCommand(CanExecute = nameof(CanFindTypeDefinition))]
    private Task LanguageTypeDefinitionAsync(CancellationToken token) => NavigateAsync(_features.Language.TypeDefinition, token);

    [RelayCommand(CanExecute = nameof(CanFindImplementation))]
    private Task LanguageImplementationAsync(CancellationToken token) => NavigateAsync(_features.Language.Implementation, token);

    [RelayCommand(CanExecute = nameof(CanFindReferences))]
    private Task LanguageReferencesAsync(CancellationToken token) => PresentAsync(_features.Language.References,
        new LanguageServerFeatures.ReferencesInput(Capture(), EditorPosition.Caret), LanguageEditorResult.Navigation, token);

    [RelayCommand(CanExecute = nameof(CanReadSymbols))]
    private Task LanguageSymbolsAsync(CancellationToken token) => PresentAsync(_features.Language.Symbols,
        new LanguageServerFeatures.DocumentInput(Capture()), result => LanguageEditorResult.Symbols(result, new Uri(FullPath).AbsoluteUri), token);

    [RelayCommand(CanExecute = nameof(CanReadDiagnostics))]
    private Task LanguageDiagnosticsAsync(CancellationToken token) => PresentAsync(_features.Language.Diagnostics,
        new LanguageServerFeatures.DocumentInput(Capture()), result => LanguageEditorResult.Diagnostics(result, new Uri(FullPath).AbsoluteUri), token);

    [RelayCommand(CanExecute = nameof(CanFormat))]
    private Task LanguageFormatAsync(CancellationToken token)
    {
        var position = EditorPosition;
        return PresentAsync(_features.Language.Format, new LanguageServerFeatures.FormatInput(Capture(),
            Offset: position.SelectionLength > 0 ? position.SelectionStart : null,
            EndOffset: position.SelectionLength > 0 ? position.SelectionStart + position.SelectionLength : null), LanguageEditorResult.Preview, token);
    }

    private bool CanRenameLanguageSymbol() =>
        LanguageCapabilities.Supports(LspDocumentMethods.Rename) && !string.IsNullOrWhiteSpace(LanguageRenameText);

    [RelayCommand(CanExecute = nameof(CanRenameLanguageSymbol))]
    private Task LanguageRenameAsync(CancellationToken token) => PresentAsync(_features.Language.Rename,
        new LanguageServerFeatures.RenameInput(Capture(), EditorPosition.Caret, LanguageRenameText ?? ""), LanguageEditorResult.Preview, token);

    [RelayCommand(CanExecute = nameof(CanReadCodeActions))]
    private Task LanguageCodeActionsAsync(CancellationToken token)
    {
        var position = EditorPosition;
        var start = position.SelectionLength == 0 ? position.Caret : position.SelectionStart;
        return PresentAsync(_features.Language.CodeActions,
            new LanguageServerFeatures.CodeActionsInput(Capture(), start, start + position.SelectionLength), LanguageEditorResult.Actions, token);
    }

    private bool CanOpenLanguageResult() => SelectedLanguageRow is { Request: not null };
    private bool CanApplyLanguageEdit() => LanguageResult is { EditPreviewId: not null };

    [RelayCommand(CanExecute = nameof(CanOpenLanguageResult))]
    private Task OpenLanguageResultAsync(CancellationToken token) => SelectedLanguageRow?.Request switch
    {
        LanguageServerFeatures.OpenLocationInput target => SendAsync(_features.Language.OpenLocation.Id, target, token),
        LanguageServerFeatures.PreviewCompletionInput completion => InsertCompletionAsync(completion, token),
        LanguageServerFeatures.PreviewActionInput action => PresentAsync(_features.Language.PreviewAction, action, LanguageEditorResult.Preview, token),
        _ => Task.CompletedTask,
    };

    private async Task InsertCompletionAsync(LanguageServerFeatures.PreviewCompletionInput input, CancellationToken token)
    {
        ClearLanguagePresentation();
        var request = _languageRequest;
        var result = await SendAsync(_features.Language.PreviewCompletion.Id, input, token).ConfigureAwait(true);
        if (request != _languageRequest || result is not { Code: "ok", Data: { } data })
            return;

        if (data.Deserialize<LanguageEditPreview>(WireJson.Options) is { } preview && preview.Targets is
            [{ Edits: [var edit] } target] && target.RootId == RootId &&
            string.Equals(target.RelativePath, RelativePath, StringComparison.Ordinal))
        {
            var applied = await SendAsync(_features.Language.ApplyEdit.Id, new LanguageServerFeatures.ApplyEditInput(input.Document, preview.PreviewId), token)
                .ConfigureAwait(true);
            if (applied?.Code == "ok" && Revision == input.Document.Revision + 1)
                ShowRange(edit.Offset + edit.NewText.Length, 0);
            return;
        }
        LanguageResult = LanguageEditorResult.Preview(data);
    }

    [RelayCommand(CanExecute = nameof(CanApplyLanguageEdit))]
    private async Task ApplyLanguageEditAsync(CancellationToken token)
    {
        if (LanguageResult is not { EditPreviewId: { } id } review)
            return;

        var result = await SendAsync(_features.Language.ApplyEdit.Id, new LanguageServerFeatures.ApplyEditInput(Capture(), id), token)
            .ConfigureAwait(true);
        if (ReferenceEquals(LanguageResult, review) && result?.Code is "ok" or "document_changed" or "language_edit_unavailable")
            ClearLanguagePresentation();
    }

    private Task QueryAtCaretAsync(Feature<LanguageServerFeatures.CompleteInput> command,
        Func<JsonElement, LanguageEditorResult> project, CancellationToken token) => PresentAsync(command,
        new LanguageServerFeatures.CompleteInput(Capture(), EditorPosition.Caret), project, token);

    private Task NavigateAsync(Feature<LanguageServerFeatures.CompleteInput> command, CancellationToken token) =>
        QueryAtCaretAsync(command, LanguageEditorResult.Navigation, token);

    private async Task PresentAsync<T>(Feature<T> feature, T input, Func<JsonElement, LanguageEditorResult> project,
        CancellationToken token) where T : class
    {
        ClearLanguagePresentation();
        var request = _languageRequest;
        var result = await SendAsync(feature.Id, input, token).ConfigureAwait(true);
        if (request != _languageRequest || result is not { Code: "ok", Data: { } data })
            return;

        LanguageResult = project(data);
        SelectedLanguageRow = LanguageResult?.Rows.FirstOrDefault(row => row.Request is not null);
    }

    private void ClearLanguagePresentation()
    {
        _languageRequest++;
        SelectedLanguageRow = null;
        LanguageResult = null;
    }
}
