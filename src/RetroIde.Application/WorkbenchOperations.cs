using Microsoft.Extensions.Logging;

namespace RetroIde.Application;

internal interface ITrackedWorkbenchDispatcher
{
    Task<OperationResult> StartBackgroundAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class;
}

internal interface IWorkbenchOperationRegistration
{
    WorkbenchOperations Operations { get; }
}

internal sealed record WorkbenchOperationStatus(Guid OperationId, string Tool, string State, bool CanCancel,
    DateTimeOffset AcceptedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

/// <summary>Owns detached operations for one live workbench registration.</summary>
internal sealed partial class WorkbenchOperations(ILogger logger) : IDisposable
{
    private const int MaximumPending = 8;
    private const int MaximumRetained = 64;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Operation> _operations = [];
    private bool _closed;

    internal FeatureOutcome Start(Guid id, string tool, ActorGrant owner, Func<CancellationToken, Task<OperationResult>> execute)
    {
        Operation operation;
        WorkbenchOperationStatus accepted;
        lock (_gate)
        {
            if (_closed)
                return new("view_closed", "The workbench has closed.");

            Prune(DateTimeOffset.UtcNow);
            if (_operations.Values.Count(item => item.Result is null) >= MaximumPending)
                return new("operation_limit", "Wait for an active workbench operation to finish or cancel one.");
            if (_operations.Count >= MaximumRetained)
            {
                var oldest = _operations.Values.Where(item => item.Result is not null).MinBy(item => item.FinishedAt)!;
                _operations.Remove(oldest.Id);
            }

            operation = new Operation(id, tool, owner);
            _operations.Add(id, operation);
            accepted = operation.Snapshot();
        }
        _ = Task.Run(() => RunAsync(operation, execute));
        return FeatureOutcome.Success("Workbench operation accepted.", accepted);
    }

    internal WorkbenchOperationStatus? Status(Guid id, ActorGrant owner)
    {
        lock (_gate)
            return Find(id, owner)?.Snapshot();
    }

    internal OperationResult? Result(Guid id, ActorGrant owner, out bool exists)
    {
        lock (_gate)
        {
            var operation = Find(id, owner);
            exists = operation is not null;
            return operation?.Result;
        }
    }

    internal WorkbenchOperationStatus? Cancel(Guid id, ActorGrant owner)
    {
        lock (_gate)
        {
            var operation = Find(id, owner);
            if (operation is { Result: null })
                RequestCancellation(operation);
            return operation?.Snapshot();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            foreach (var operation in _operations.Values.Where(item => item.Result is null))
                RequestCancellation(operation);
            _operations.Clear();
        }
    }

    private Operation? Find(Guid id, ActorGrant owner)
    {
        Prune(DateTimeOffset.UtcNow);
        return _operations.TryGetValue(id, out var operation) && ReferenceEquals(operation.Owner, owner)
            ? operation : null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var expired in _operations.Values.Where(item => item.FinishedAt <= now - Retention).ToArray())
            _operations.Remove(expired.Id);
    }

    private void RequestCancellation(Operation operation)
    {
        operation.CancellationCallbacks ??= CancelAsync(operation);
    }

    private async Task CancelAsync(Operation operation)
    {
        try { await operation.Cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, $"Cancel workbench operation {operation.Id}");
        }
    }

    private async Task RunAsync(Operation operation, Func<CancellationToken, Task<OperationResult>> execute)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, operation.Owner.RevocationToken);
        OperationResult result;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            lock (_gate)
                operation.StartedAt = DateTimeOffset.UtcNow;
            result = await execute(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            result = OperationResult.Failure(operation.Id, "cancelled", "The operation was cancelled.");
        }
        catch (Exception error)
        {
            var recoverable = RuntimeFailures.CanRecover(error);
            RuntimeFailures.Report(logger, error, $"Background workbench operation {operation.Id}", recoverable ? LogLevel.Error : LogLevel.Critical);
            result = OperationResult.Failure(operation.Id, "internal_error", "The operation failed. Check the application log for details.");
            if (!recoverable)
            {
                await FinishAsync(operation, result).ConfigureAwait(false);
                throw;
            }
        }

        await FinishAsync(operation, result).ConfigureAwait(false);
    }

    private async Task FinishAsync(Operation operation, OperationResult result)
    {
        Task? cancellation;
        lock (_gate)
        {
            operation.Result = result;
            operation.FinishedAt = DateTimeOffset.UtcNow;
            cancellation = operation.CancellationCallbacks;
        }
        if (cancellation is not null)
            await cancellation.ConfigureAwait(false);
        operation.Cancellation.Dispose();
        LogFinished(logger, operation.Tool, operation.Owner.Actor, operation.Owner.WorkspaceId, operation.Owner.ViewKey, operation.Id, result.Code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Workbench operation {Tool} actor={Actor} workspace={Workspace} view={View} operation={Operation} outcome={Outcome}")]
    private static partial void LogFinished(ILogger logger, string tool, string actor, Guid workspace, Guid view, Guid operation, string outcome);

    private sealed class Operation
    {
        internal Operation(Guid id, string tool, ActorGrant owner)
        {
            Id = id;
            Tool = tool;
            Owner = owner;
            Token = Cancellation.Token;
        }

        internal Guid Id { get; }
        internal string Tool { get; }
        internal ActorGrant Owner { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token { get; }
        internal Task? CancellationCallbacks { get; set; }
        internal DateTimeOffset AcceptedAt { get; } = DateTimeOffset.UtcNow;
        internal DateTimeOffset? StartedAt { get; set; }
        internal DateTimeOffset? FinishedAt { get; set; }
        internal OperationResult? Result { get; set; }

        internal WorkbenchOperationStatus Snapshot()
        {
            var state = Result?.Code switch
            {
                "ok" => "succeeded",
                "cancelled" => "cancelled",
                not null => "failed",
                _ when Token.IsCancellationRequested => "cancelling",
                _ when StartedAt is not null => "running",
                _ => "queued",
            };
            return new WorkbenchOperationStatus(Id, Tool, state, Result is null && !Token.IsCancellationRequested,
                AcceptedAt, StartedAt, FinishedAt);
        }
    }
}
