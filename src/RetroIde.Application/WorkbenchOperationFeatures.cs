namespace RetroIde.Application;

internal sealed class WorkbenchOperationFeatures
{
    internal sealed record HandleInput(Guid OperationId);

    internal Feature<HandleInput> Status { get; } = new("workbench.operation.status", "workbench_operation_status",
        "Read the state and cancellation availability of a background operation started by this attachment.", false,
        static (input, view, actor) => (view as IWorkbenchOperationRegistration)?.Operations.Status(input.OperationId, actor) is { } status
            ? FeatureOutcome.Success("Workbench operation state read.", status) : Unavailable(), typeof(WorkbenchOperationStatus));

    internal Feature<HandleInput> Result { get; } = new("workbench.operation.result", "workbench_operation_result",
        "Retrieve the original typed result of a finished background operation started by this attachment.", false,
        ReadResult, typeof(OperationResult));

    internal Feature<HandleInput> Cancel { get; } = new("workbench.operation.cancel", "workbench_operation_cancel",
        "Request cancellation of a background operation started by this attachment.", true,
        static (input, view, actor) => (view as IWorkbenchOperationRegistration)?.Operations.Cancel(input.OperationId, actor) is { } status
            ? FeatureOutcome.Success("Workbench operation state read after the cancellation request.", status)
            : Unavailable(), typeof(WorkbenchOperationStatus));

    private static FeatureOutcome ReadResult(HandleInput input, IWorkbenchRegistration view, ActorGrant actor)
    {
        if (view is not IWorkbenchOperationRegistration registration)
            return Unavailable();
        var result = registration.Operations.Result(input.OperationId, actor, out var exists);
        return result is not null
            ? FeatureOutcome.Success("Workbench operation result read.", result)
            : exists ? new("operation_pending", "The workbench operation is still running.") : Unavailable();
    }

    private static FeatureOutcome Unavailable() => new("operation_unavailable", "The operation is unavailable to this attachment.");
}
