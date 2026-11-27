using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerFeatures
{
    internal sealed record FormatInput(DocumentContext Document, int TabSize = 4, bool InsertSpaces = true,
        int? Offset = null, int? EndOffset = null);
    internal sealed record RenameInput(DocumentContext Document, int Offset, string NewName);
    internal sealed record CodeActionsInput(DocumentContext Document, int Offset, int EndOffset, string[]? Only = null);
    internal sealed record PreviewActionInput(DocumentContext Document, Guid OfferId, int Index);
    internal sealed record ApplyEditInput(DocumentContext Document, Guid PreviewId);

    internal Feature<FormatInput> Format { get; } = new("language.document.format", "language_format",
        "Preview whole-document or range formatting for the captured editor revision.",
        FormatAsync, typeof(LanguageEditPreview));

    internal Feature<RenameInput> Rename { get; } = new("language.document.rename", "language_rename",
        "Preview the source edits for a language-server symbol rename.",
        RenameAsync, typeof(LanguageEditPreview));

    internal Feature<CodeActionsInput> CodeActions { get; } = new("language.document.code-actions", "language_code_actions",
        "Read the offered code actions for an editor range and its current diagnostics.",
        ReadCodeActionsAsync, typeof(LanguageCodeActionList));

    internal Feature<PreviewActionInput> PreviewAction { get; } = new("language.document.code-action.preview", "language_preview_code_action",
        "Resolve an offered code action and preview the source edits it supplies.",
        PreviewCodeActionAsync, typeof(LanguageEditPreview));

    internal Feature<ApplyEditInput> ApplyEdit { get; } = new("language.document.edit.apply", "language_apply_edit",
        "Apply a prepared workspace edit to its validated live editor buffers.",
        ApplyEditAsync, typeof(DocumentContext[]), mutation: true);

    private static Task<FeatureOutcome> FormatAsync(FormatInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (input.TabSize is < 1 or > 32 || (input.Offset is null) != (input.EndOffset is null) ||
            input.Offset is { } start && input.EndOffset < start)
            return Task.FromResult(new FeatureOutcome("invalid_language_range", "Use a valid formatting range and tab size."));
        if (view is not ILanguageRegistration registration || registration.ReadLanguageDocument(input.Document) is not { } document)
            return Task.FromResult(DocumentChanged());
        var selection = input.Offset is not null;
        var request = new LanguageRequest(selection ? "textDocument/rangeFormatting" : "textDocument/formatting",
            selection ? "documentRangeFormattingProvider" : "documentFormattingProvider", Parameters: context =>
            {
                var options = new { input.TabSize, input.InsertSpaces };
                return input.Offset is { } offset
                    ? new { textDocument = context.Document, options, range = new LanguageRange(context.Position(offset), context.Position(input.EndOffset!.Value)) }
                    : new { textDocument = context.Document, options };
            });
        return QueryAsync(input.Document, request, view, token,
            (result, live, cancellation) => PrepareEditAsync(result, live, cancellation, LanguageServerSession.FileUri(document.Path)),
            captureBuffers: true);
    }

    private static Task<FeatureOutcome> RenameAsync(RenameInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.NewName) || input.NewName.Length > 256 || input.NewName.Contains('\0'))
            return Task.FromResult(new FeatureOutcome("invalid_language_name", "Enter a valid new symbol name."));
        return QueryAsync(input.Document, new("textDocument/rename", "renameProvider", Parameters: context =>
            new { textDocument = context.Document, position = context.Position(input.Offset), input.NewName }),
            view, token, static (result, registration, cancellation) => PrepareEditAsync(result, registration, cancellation),
            captureBuffers: true);
    }

    private static Task<FeatureOutcome> ReadCodeActionsAsync(CodeActionsInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (input.EndOffset < input.Offset)
            return Task.FromResult(new FeatureOutcome("invalid_language_range", "Use an ordered editor range."));
        if (view is not ILanguageRegistration registration || registration.ReadLanguageDocument(input.Document) is not { } document)
            return Task.FromResult(DocumentChanged());

        var diagnostics = registration.LanguageServers.Diagnostics(document).Items;
        return QueryAsync(input.Document, new("textDocument/codeAction", "codeActionProvider", Parameters: context =>
        {
            object actionContext = input.Only is { } only ? new { diagnostics, only } : new { diagnostics };
            return new
            {
                textDocument = context.Document,
                range = new LanguageRange(context.Position(input.Offset), context.Position(input.EndOffset)),
                context = actionContext,
            };
        }), view, token, StoreActionsAsync, captureBuffers: true);
    }

    private static Task<FeatureOutcome> StoreActionsAsync(LanguageQueryResult result, ILanguageRegistration registration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (result.Result.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
            throw new InvalidDataException("The language server returned an invalid code action list.");

        var actions = result.Result.ValueKind == JsonValueKind.Null ? []
            : result.Result.EnumerateArray().Take(128).ToArray();
        var offer = new LanguageCodeActionList(Guid.NewGuid(), result.Document, result.Server, actions)
        {
            CapturedBuffers = result.CapturedBuffers,
        };
        return Task.FromResult(new FeatureOutcome("ok", "Code actions prepared.")
        {
            Commit = _ =>
            {
                registration.LanguageServers.Actions.Add(offer.OfferId, offer);
                return FeatureOutcome.Success("Code actions read.", offer);
            },
        });
    }

    private static Task<FeatureOutcome> PreviewCodeActionAsync(PreviewActionInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not ILanguageRegistration registration || registration.ReadLanguageDocument(input.Document) is not { } origin ||
            registration.LanguageServers.Actions.Find(input.OfferId) is not { } offer || offer.Document != input.Document ||
            offer.Server.SessionId is not { } serverId || registration.LanguageServers.ReadSession(origin, serverId) is null)
            return Task.FromResult(DocumentChanged());
        if (input.Index < 0 || input.Index >= offer.Actions.Length || offer.Actions[input.Index].ValueKind != JsonValueKind.Object)
            return Task.FromResult(new FeatureOutcome("language_action_unavailable", "Select an offered code action."));
        var action = offer.Actions[input.Index];
        if (action.TryGetProperty("edit", out _) || action.TryGetProperty("disabled", out _) || action.TryGetProperty("command", out _))
            return PrepareActionAsync(new(input.Document, offer.Server, action) { CapturedBuffers = offer.CapturedBuffers }, registration, token);

        return QueryAsync(input.Document, new("codeAction/resolve", "codeActionProvider", Parameters: _ => action),
            view, token, (result, live, cancellation) =>
                PrepareActionAsync(result with { CapturedBuffers = offer.CapturedBuffers }, live, cancellation));
    }

    private static Task<FeatureOutcome> PrepareActionAsync(LanguageQueryResult result, ILanguageRegistration registration, CancellationToken token)
    {
        if (result.Result.ValueKind != JsonValueKind.Object || result.Result.TryGetProperty("disabled", out _) ||
            result.Result.TryGetProperty("command", out _) || !result.Result.TryGetProperty("edit", out var edit))
            return Task.FromResult(new FeatureOutcome("language_action_unavailable", "The code action has no supported text edit."));
        return PrepareEditAsync(result with { Result = edit }, registration, token);
    }

    private static async Task<FeatureOutcome> PrepareEditAsync(LanguageQueryResult result, ILanguageRegistration registration,
        CancellationToken token, string? formattingUri = null)
    {
        LanguageEditPlan plan;
        try { plan = await LanguageEditPlanner.PrepareAsync(result, registration, token, formattingUri).ConfigureAwait(false); }
        catch (LanguageDocumentChangedException) { return DocumentChanged(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentOutOfRangeException)
        {
            registration.ReportLanguageFailure(error, "Validate language server workspace edit");
            return new("invalid_workspace_edit", "The workspace edit includes an unsupported target, range, or operation.");
        }
        var preview = plan.Describe();
        return new FeatureOutcome("ok", "Language edit prepared.")
        {
            Commit = current => current is ILanguageRegistration active && CanApply(active, plan)
                ? PublishPreview(active, plan, preview) : DocumentChanged(),
        };
    }

    private static FeatureOutcome PublishPreview(ILanguageRegistration active, LanguageEditPlan plan, LanguageEditPreview preview)
    {
        active.LanguageServers.Edits.Add(plan.Id, plan);
        return FeatureOutcome.Success("Language edit preview ready.", preview);
    }

    private static bool CanApply(ILanguageRegistration registration, LanguageEditPlan plan) =>
        registration.HasExplorer && registration.CanChangeExplorer && plan.Server.SessionId is { } serverId &&
        registration.ReadLanguageDocument(plan.Origin) is { } origin &&
        registration.LanguageServers.ReadSession(origin, serverId) is not null &&
        plan.Documents.All(item => item.File.IsCurrent(registration));

    private static async Task<FeatureOutcome> ApplyEditAsync(ApplyEditInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not ILanguageRegistration registration || registration.LanguageServers.Edits.Find(input.PreviewId) is not { } plan ||
            plan.Origin != input.Document || !CanApply(registration, plan))
            return DocumentChanged();

        if (!await LanguageEditPlanner.RevalidateClosedFilesAsync(plan.Documents, token).ConfigureAwait(false))
            return DocumentChanged();
        return new FeatureOutcome("ok", "Language edit validated.")
        {
            Commit = current => current is ILanguageRegistration active &&
                ReferenceEquals(active.LanguageServers.Edits.Find(input.PreviewId), plan) && CanApply(active, plan)
                    ? CommitEdit(active, plan) : DocumentChanged(),
        };
    }

    private static FeatureOutcome CommitEdit(ILanguageRegistration active, LanguageEditPlan plan)
    {
        var outcome = active.ApplyLanguageEdits(plan.Documents);
        if (string.Equals(outcome.Code, "ok", StringComparison.Ordinal))
            active.LanguageServers.Edits.Remove(plan.Id);
        return outcome;
    }
}
