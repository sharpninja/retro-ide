using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerFeatures
{
    internal sealed record PreviewCompletionInput(DocumentContext Document, Guid OfferId, int Index);

    internal Feature<PreviewCompletionInput> PreviewCompletion { get; } = new("language.document.completion.preview", "language_preview_completion",
        "Preview a text completion chosen from the current document's completion offer.",
        PreviewCompletionAsync, typeof(LanguageEditPreview));

    private static Task<FeatureOutcome> StoreCompletionsAsync(LanguageQueryResult query, ILanguageRegistration registration,
        int offset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entries = LanguageCompletionOffer.ReadItems(query.Result);
        if (entries.Length == 0)
            return Task.FromResult(FeatureOutcome.Success("No language completions returned.", query));

        var offerId = Guid.NewGuid();
        var offered = query with { CompletionOfferId = offerId };
        return Task.FromResult(new FeatureOutcome("ok", "Language completions prepared.")
        {
            Commit = _ =>
            {
                registration.LanguageServers.Completions.Add(offerId, new(offered, offset, entries));
                return FeatureOutcome.Success("Language completions read.", offered);
            },
        });
    }

    private static Task<FeatureOutcome> PreviewCompletionAsync(PreviewCompletionInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (view is not ILanguageRegistration registration || registration.ReadLanguageDocument(input.Document) is not { } origin ||
            registration.LanguageServers.Completions.Find(input.OfferId) is not { } offer || offer.Query.Document != input.Document ||
            offer.Query.Server.SessionId is not { } serverId || registration.LanguageServers.ReadSession(origin, serverId) is null)
            return Task.FromResult(DocumentChanged());
        if (input.Index < 0 || input.Index >= offer.Items.Length)
            return Task.FromResult(new FeatureOutcome("language_completion_unavailable", "Select an offered completion."));

        try
        {
            var edits = offer.Prepare(input.Index, origin.Text);
            return PrepareEditAsync(offer.Query with { Result = edits }, registration, token, LanguageServerSession.FileUri(origin.Path));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentOutOfRangeException)
        {
            return Task.FromResult(new FeatureOutcome("invalid_language_edit", "The completion contains an unsupported source edit."));
        }
    }
}
