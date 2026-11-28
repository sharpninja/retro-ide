using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    private ValueTask<AdapterResult<T>> InvokePortAsync<TPort, T>(SessionOperation scope, CapabilityId capability,
        string? subject, string operation, Func<TPort, CapabilityDescriptor, ValueTask<AdapterResult<T>>> invoke, CancellationToken token,
        Func<CapabilityDescriptor, CancellationToken, ValueTask<AdapterError?>>? check = null)
        where TPort : class, IEmulatorPort where T : class =>
        InvokePortAsync<TPort, T>(scope, capability, subject, operation, selection => invoke(selection.Port, selection.Descriptor), token, check);

    private async ValueTask<AdapterResult<T>> InvokePortAsync<TPort, T>(SessionOperation scope, CapabilityId capability,
        string? subject, string operation, Func<PreparedPort<TPort>, ValueTask<AdapterResult<T>>> invoke, CancellationToken token,
        Func<CapabilityDescriptor, CancellationToken, ValueTask<AdapterError?>>? check = null)
        where TPort : class, IEmulatorPort where T : class
    {
        if (ValidateScope(scope) is { } invalid)
            return AdapterResult<T>.Failure(invalid);

        try
        {
            var result = await WithPreparedPortAsync(scope, capability, subject, operation, check, invoke, token).ConfigureAwait(false);
            if (result.Succeeded)
                return result;
            return result.Error.OperationId is null || result.Error.OperationId == scope.OperationId
                ? AdapterResult<T>.Failure(result.Error with { OperationId = scope.OperationId })
                : Fail<T>(AdapterErrorCodes.Incompatible, "The emulator returned an unrelated operation result.", scope.OperationId);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail<T>(AdapterErrorCodes.Cancelled, "The emulator operation was cancelled.", scope.OperationId);
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, $"query emulator capability {capability.Name}");
            return Fail<T>(AdapterErrorCodes.Disconnected, "The emulator operation failed.", scope.OperationId);
        }
    }

    private async ValueTask<AdapterResult<T>> WithPreparedPortAsync<TPort, T>(SessionOperation scope,
        CapabilityId capability, string? subject, string operation, Func<CapabilityDescriptor, CancellationToken, ValueTask<AdapterError?>>? check,
        Func<PreparedPort<TPort>, ValueTask<AdapterResult<T>>> invoke, CancellationToken token, bool requireRevision = false)
        where TPort : class, IEmulatorPort where T : class
    {
        var snapshot = await ReadAsync(token).ConfigureAwait(false);
        if (!snapshot.Succeeded)
            return AdapterResult<T>.Failure(snapshot.Error with { OperationId = scope.OperationId });

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var state = State;
            if (CheckPortState(state, scope, requireRevision) is { } invalid)
                return AdapterResult<T>.Failure(invalid);
            var descriptor = Advertised(state, capability, subject, operation);
            if (descriptor is null)
                return Fail<T>(AdapterErrorCodes.Unsupported, "This operation is not advertised for the selected target.", scope.OperationId);

            var map = Volatile.Read(ref _mapRevision);
            if (check is not null)
            {
                var error = await check(descriptor, token).ConfigureAwait(false);
                var latest = State;
                // A nested description read may publish an equivalent copy of the current state.
                // Compare the host's revisions and cursor before using the completed check.
                if (!SameRoutingState(state, latest) || (error is null && map != Volatile.Read(ref _mapRevision)))
                    continue;
                if (error is not null)
                    return AdapterResult<T>.Failure(error);
            }

            if (!_connection.TryGetPort<TPort>(capability, out var port))
                return Fail<T>(AdapterErrorCodes.Unsupported, "This operation is not advertised for the selected target.", scope.OperationId);
            if (!SameRoutingState(state, State) || (check is not null && map != Volatile.Read(ref _mapRevision)))
                continue;
            token.ThrowIfCancellationRequested();
            if (_sessionLifetime.IsCancellationRequested)
                return Fail<T>(AdapterErrorCodes.Disconnected, "The emulator connection has closed.", scope.OperationId);
            return await invoke(new(port, descriptor, state)).ConfigureAwait(false);
        }
    }

    private AdapterError? CheckPortState(SessionState state, SessionOperation scope, bool requireRevision = false)
    {
        if (_sessionLifetime.IsCancellationRequested || state.Lifecycle != SessionLifecycle.Connected)
            return new(AdapterErrorCodes.Disconnected, "The emulator session is disconnected.", scope.OperationId);
        if (requireRevision && scope.ExpectedRevision is null)
            return new(AdapterErrorCodes.InvalidArgument, "This mutation requires an expected state revision.", scope.OperationId);
        return scope.ExpectedRevision is { } expected && expected != state.StateRevision
            ? new(AdapterErrorCodes.RevisionConflict, "The request used an old state revision.", scope.OperationId, state.StateRevision)
            : null;
    }

    private static bool SameRoutingState(SessionState left, SessionState right) =>
        left.StateRevision == right.StateRevision && left.Cursor == right.Cursor &&
        left.Lifecycle == right.Lifecycle && left.Capabilities.Revision == right.Capabilities.Revision;

    private sealed record PreparedPort<TPort>(TPort Port, CapabilityDescriptor Descriptor, SessionState State) where TPort : class, IEmulatorPort;
}
