using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal ValueTask<AdapterResult<MutationReceipt>> ExecuteAsync(MutationRequest<ExecutionCommand> request, CancellationToken token)
    {
        var scope = request.Scope;
        if (ValidateScope(scope) is { } invalid)
            return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(invalid));
        if (!Enum.IsDefined(request.Payload.Action) || request.Payload.Count == 0 ||
            (request.Payload.Action is not (ExecutionAction.StepInstruction or ExecutionAction.StepCycle or ExecutionAction.StepPhase) &&
                request.Payload.Count != 1))
            return ValueTask.FromResult(Fail<MutationReceipt>(AdapterErrorCodes.InvalidArgument, "The execution action or count is invalid.", scope.OperationId));

        var target = new PendingMutation(scope, IExecutionPort.Capability, request.Payload.SubjectId, request.Payload.Operation, request.Payload);
        return MutatePortAsync<IExecutionPort>(target, (port, cancellation) => port.ExecuteAsync(request, cancellation),
            (capability, _) => ValueTask.FromResult<AdapterError?>(capability.Limits.TryGetValue("max-count", out var max) && request.Payload.Count > max
                ? new(AdapterErrorCodes.InvalidArgument, "The execution count exceeds the advertised limit.", scope.OperationId) : null),
            token);
    }

    private async ValueTask<AdapterResult<MutationReceipt>> MutatePortAsync<TPort>(PendingMutation target,
        Func<TPort, CancellationToken, ValueTask<AdapterResult<MutationReceipt>>> invoke,
        Func<CapabilityDescriptor, CancellationToken, ValueTask<AdapterError?>>? check, CancellationToken token,
        bool requireRevision = true) where TPort : class, IEmulatorPort
    {
        var scope = target.Scope;
        if (ValidateScope(scope) is { } invalid)
            return AdapterResult<MutationReceipt>.Failure(invalid);
        if (!await EnterAsync(token).ConfigureAwait(false))
            return Fail<MutationReceipt>(AdapterErrorCodes.Cancelled, "The emulator operation was cancelled before dispatch.", scope.OperationId);

        try
        {
            var retry = _resolutions.TryGetValue(scope.OperationId, out var previous);
            if (retry)
            {
                if (previous.Request.Scope != scope)
                    return Fail<MutationReceipt>(AdapterErrorCodes.PermissionDenied, "The operation ID belongs to another scope.", scope.OperationId);
                if (!previous.Request.MatchesOperation(target))
                    return Fail<MutationReceipt>(AdapterErrorCodes.InvalidArgument,
                        "A changed emulator operation must use a new operation ID.", scope.OperationId);
                if (previous.Receipt.Disposition == MutationDisposition.Applied)
                    return AdapterResult<MutationReceipt>.Success(new(scope, previous.Receipt.StateRevision));

            }
            if (_uncertain is not null)
                return Fail<MutationReceipt>(AdapterErrorCodes.UnknownOutcome,
                    "Reconcile the outstanding emulator mutation before issuing another.", scope.OperationId);

            var snapshot = await ReadAsync(token).ConfigureAwait(false);
            if (!snapshot.Succeeded)
                return AdapterResult<MutationReceipt>.Failure(snapshot.Error with { OperationId = scope.OperationId });

            var state = snapshot.Value;
            if (state.Lifecycle != SessionLifecycle.Connected)
                return Fail<MutationReceipt>(AdapterErrorCodes.Disconnected, "The emulator session is disconnected.", scope.OperationId);
            if (requireRevision && scope.ExpectedRevision is null)
                return Fail<MutationReceipt>(AdapterErrorCodes.InvalidArgument, "This mutation requires an expected state revision.", scope.OperationId);
            if (scope.ExpectedRevision is { } expected && expected != state.StateRevision)
                return Fail<MutationReceipt>(AdapterErrorCodes.RevisionConflict, "Emulator state changed before dispatch.", scope.OperationId, state.StateRevision);

            var capability = Advertised(state, target.Capability, target.SubjectId, target.Operation);
            if (capability is null || !_connection.TryGetPort<TPort>(capability.Id, out var port))
                return Fail<MutationReceipt>(AdapterErrorCodes.Unsupported, "This operation is not advertised for the selected target.", scope.OperationId);
            if (check is not null && await check(capability, token).ConfigureAwait(false) is { } rejected)
                return AdapterResult<MutationReceipt>.Failure(rejected);

            var result = await DispatchAsync(target, cancellation => invoke(port, cancellation), token).ConfigureAwait(false);
            if (retry && result.Succeeded)
                _resolutions[scope.OperationId] = (target, new(scope, MutationDisposition.Applied, result.Value.StateRevision));
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail<MutationReceipt>(AdapterErrorCodes.Cancelled, "The emulator operation was cancelled before dispatch.", scope.OperationId);
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, $"resolve emulator mutation {target.Capability.Name}");
            return Fail<MutationReceipt>(AdapterErrorCodes.Failed, "The emulator capability could not be resolved.", scope.OperationId);
        }
        finally { _mutations.Release(); }
    }

    internal async ValueTask<AdapterResult<MutationResolution>> ReconcileAsync(SessionOperation scope, CancellationToken token)
    {
        if (ValidateScope(scope) is { } invalid)
            return AdapterResult<MutationResolution>.Failure(invalid);
        if (!await EnterAsync(token).ConfigureAwait(false))
            return Fail<MutationResolution>(AdapterErrorCodes.Cancelled, "Reconciliation was cancelled.", scope.OperationId);

        try
        {
            if (_uncertain is not { } pending || pending.Scope.OperationId != scope.OperationId)
                return Fail<MutationResolution>(AdapterErrorCodes.InvalidState, "This session has no matching uncertain operation.", scope.OperationId);
            if (pending.Scope != scope)
                return Fail<MutationResolution>(AdapterErrorCodes.PermissionDenied, "Use the original actor and precondition to reconcile this operation.", scope.OperationId);

            var state = await ReadAsync(token).ConfigureAwait(false);
            if (!state.Succeeded)
                return AdapterResult<MutationResolution>.Failure(state.Error with { OperationId = scope.OperationId });
            if (state.Value.Lifecycle != SessionLifecycle.Connected)
                return Fail<MutationResolution>(AdapterErrorCodes.Disconnected, "The emulator session is disconnected.", scope.OperationId);
            if (Advertised(state.Value, IOperationReceiptPort.Capability, subject: null, IOperationReceiptPort.Reconcile) is null ||
                !_connection.TryGetPort<IOperationReceiptPort>(IOperationReceiptPort.Capability, out var port))
                return Fail<MutationResolution>(AdapterErrorCodes.Unsupported, "The emulator does not provide authoritative operation receipts.", scope.OperationId);

            var result = await port.ReconcileAsync(scope, token).ConfigureAwait(false);
            if (!result.Succeeded && (result.Error.OperationId is null || result.Error.OperationId == scope.OperationId))
                return AdapterResult<MutationResolution>.Failure(result.Error with { OperationId = scope.OperationId });
            if (!result.Succeeded || result.Value.Scope != scope || !Enum.IsDefined(result.Value.Disposition) ||
                result.Value.StateRevision < scope.ExpectedRevision.GetValueOrDefault())
                return Fail<MutationResolution>(AdapterErrorCodes.Incompatible, "The emulator returned an invalid operation resolution.", scope.OperationId);
            if (result.Value.Disposition != MutationDisposition.Unknown)
            {
                _uncertain = null;
                _resolutions[scope.OperationId] = (pending, result.Value);
            }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail<MutationResolution>(AdapterErrorCodes.Cancelled, "Reconciliation was cancelled.", scope.OperationId);
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "reconcile emulator mutation");
            return Fail<MutationResolution>(AdapterErrorCodes.Disconnected, "The emulator mutation outcome is still unknown.", scope.OperationId);
        }
        finally { _mutations.Release(); }
    }

    private async ValueTask<AdapterResult<MutationReceipt>> DispatchAsync(PendingMutation request,
        Func<CancellationToken, ValueTask<AdapterResult<MutationReceipt>>> invoke, CancellationToken token)
    {
        var scope = request.Scope;
        if (token.IsCancellationRequested)
            return Fail<MutationReceipt>(AdapterErrorCodes.Cancelled, "The emulator operation was cancelled before dispatch.", scope.OperationId);

        try
        {
            var result = await invoke(token).ConfigureAwait(false);
            if (result.Succeeded)
            {
                if (result.Value.Scope == scope && result.Value.StateRevision >= scope.ExpectedRevision.GetValueOrDefault())
                    return result;
            }
            else if ((result.Error.OperationId is null || result.Error.OperationId == scope.OperationId) &&
                     result.Error.Code is not (AdapterErrorCodes.Timeout or AdapterErrorCodes.Disconnected or AdapterErrorCodes.UnknownOutcome))
            {
                return AdapterResult<MutationReceipt>.Failure(result.Error with { OperationId = scope.OperationId });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* The request had already been dispatched. */ }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "dispatch emulator mutation");
        }

        _uncertain = request;
        return Fail<MutationReceipt>(AdapterErrorCodes.UnknownOutcome, "The emulator could have applied this operation. Reconcile before retrying.", scope.OperationId);
    }

    private async Task<bool> EnterAsync(CancellationToken token)
    {
        try { await _mutations.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        return true;
    }

    private sealed record PendingMutation(SessionOperation Scope, CapabilityId Capability, string? SubjectId, string Operation, object Payload)
    {
        internal bool MatchesOperation(PendingMutation other) => Capability == other.Capability &&
            string.Equals(SubjectId, other.SubjectId, StringComparison.Ordinal) &&
            string.Equals(Operation, other.Operation, StringComparison.Ordinal) &&
            EqualPayload(Payload, other.Payload);

        private static bool EqualPayload(object left, object right) => (left, right) switch
        {
            (MemoryWriteRequest a, MemoryWriteRequest b) =>
                a.Address == b.Address && a.Data.AsSpan().SequenceEqual(b.Data.AsSpan()),
            (IConsolePort.ChannelInput a, IConsolePort.ChannelInput b) =>
                string.Equals(a.ChannelId, b.ChannelId, StringComparison.Ordinal) && a.Data.AsSpan().SequenceEqual(b.Data.AsSpan()),
            (IRegisterPort.Update a, IRegisterPort.Update b) =>
                string.Equals(a.CpuId, b.CpuId, StringComparison.Ordinal) && a.Values.Length == b.Values.Length &&
                a.Values.Zip(b.Values).All(static pair => EqualRegisterValue(pair.First, pair.Second)),
            (IInputPort.Batch a, IInputPort.Batch b) =>
                a.Target == b.Target && a.FirstSequence == b.FirstSequence && a.Events.Length == b.Events.Length &&
                a.Events.Zip(b.Events).All(static pair => EqualInput(pair.First, pair.Second)),
            _ => Equals(left, right),
        };

        private static bool EqualRegisterValue(IRegisterPort.RegisterValue left, IRegisterPort.RegisterValue right) =>
            string.Equals(left.Id, right.Id, StringComparison.Ordinal) && left.WidthBits == right.WidthBits &&
            left.Writable == right.Writable && left.Value.AsSpan().SequenceEqual(right.Value.AsSpan());

        private static bool EqualInput(IInputPort.InputEvent left, IInputPort.InputEvent right) =>
            left is IInputPort.InputEvent.Controller a && right is IInputPort.InputEvent.Controller b
                ? a.Buttons == b.Buttons && a.Axes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .SequenceEqual(b.Axes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                : left == right;
    }
}
