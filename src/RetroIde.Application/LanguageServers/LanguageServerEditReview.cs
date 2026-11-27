namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageServerEditPreview(Guid ReviewId, string Label, LanguageServerStatus Server, LanguageEditTarget[] Targets);

internal sealed class LanguageServerEditReview(string label, LanguageServerStatus server, LanguageBufferEdit[] documents,
    CancellationToken lifetime)
{
    private readonly Lock _decision = new();
    private readonly TaskCompletionSource<LanguageServerEditReply> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly LanguageServerEditPreview _preview = new(Guid.NewGuid(), label, server, LanguageEditPlan.DescribeTargets(documents));

    internal Guid Id => _preview.ReviewId;
    internal LanguageServerStatus Server { get; } = server;
    internal LanguageBufferEdit[] Documents { get; } = documents;
    internal bool IsPending => !_reply.Task.IsCompleted && !lifetime.IsCancellationRequested;
    internal Task<LanguageServerEditReply> Reply => _reply.Task;

    internal LanguageServerEditPreview Describe() => _preview;

    internal FeatureOutcome Decide(Func<FeatureOutcome> decision, bool apply)
    {
        lock (_decision)
        {
            if (!IsPending)
                return new("language_review_unavailable", "The language server edit is no longer pending.");

            var outcome = decision();
            var applied = apply && string.Equals(outcome.Code, "ok", StringComparison.Ordinal);
            _reply.TrySetResult(new(applied, applied ? null : outcome.Summary));
            return outcome;
        }
    }

    internal void Cancel() => Reject("The workspace edit review expired or the server closed.");

    internal void Reject(string reason)
    {
        lock (_decision)
            _reply.TrySetResult(new(false, reason));
    }
}
