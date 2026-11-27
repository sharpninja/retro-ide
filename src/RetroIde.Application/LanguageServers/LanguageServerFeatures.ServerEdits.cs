using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerFeatures
{
    internal sealed record DecideServerEditInput(Guid ReviewId);

    internal Feature<GetContextInput> ListServerEdits { get; } = new("language.server-edits.list", "language_list_server_edits",
        "List pending server-originated workspace edits and their original and replacement text.",
        false, static (_, view) => view is ILanguageRegistration registration
            ? FeatureOutcome.Success("Pending server edits read.", registration.LanguageServers.ServerEditPreviews()) : Unavailable(),
        typeof(LanguageServerEditPreview[]));

    internal Feature<DecideServerEditInput> ApplyServerEdit { get; } = new("language.server-edits.apply", "language_apply_server_edit",
        "Apply a reviewed server-originated workspace edit to current source buffers.",
        ApplyServerEditAsync, typeof(DocumentContext[]), mutation: true);

    internal Feature<DecideServerEditInput> DeclineServerEdit { get; } = new("language.server-edits.decline", "language_decline_server_edit",
        "Decline a pending server-originated workspace edit.", true,
        static (input, view) => view is ILanguageRegistration registration &&
            registration.LanguageServers.FindServerEdit(input.ReviewId) is { } review
                ? review.Decide(static () => new("ok", "The workspace edit was declined."), apply: false)
                : UnknownReview());

    private static async Task<FeatureOutcome> ApplyServerEditAsync(DecideServerEditInput input,
        IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not ILanguageRegistration registration || registration.LanguageServers.FindServerEdit(input.ReviewId) is not { } review)
            return UnknownReview();

        var filesCurrent = await LanguageEditPlanner.RevalidateClosedFilesAsync(review.Documents, token).ConfigureAwait(false);
        return new FeatureOutcome("ok", "Server edit validated.")
        {
            Commit = current => current is ILanguageRegistration { HasExplorer: true, CanChangeExplorer: true } active
                ? review.Decide(() => ApplyIfCurrent(active), apply: true) : UnknownReview(),
        };

        FeatureOutcome ApplyIfCurrent(ILanguageRegistration active) =>
            filesCurrent && active.LanguageServers.IsServerEditCurrent(review) &&
            review.Documents.All(target => target.File.IsCurrent(active))
                ? active.ApplyLanguageEdits(review.Documents) : DocumentChanged();
    }

    private static FeatureOutcome UnknownReview() => new("language_review_unavailable", "The language server edit is no longer pending.");
}
