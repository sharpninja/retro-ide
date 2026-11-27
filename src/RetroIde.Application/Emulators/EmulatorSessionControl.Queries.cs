using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    private async ValueTask<AdapterResult<T>> InvokePortAsync<TPort, T>(SessionOperation scope, CapabilityId capability,
        string? subject, string operation, Func<TPort, CapabilityDescriptor, ValueTask<AdapterResult<T>>> invoke, CancellationToken token)
        where TPort : class, IEmulatorPort where T : class
    {
        if (ValidateScope(scope) is { } invalid)
            return AdapterResult<T>.Failure(invalid);

        try
        {
            var snapshot = await ReadAsync(token).ConfigureAwait(false);
            if (!snapshot.Succeeded)
                return AdapterResult<T>.Failure(snapshot.Error with { OperationId = scope.OperationId });
            var state = snapshot.Value;
            if (state.Lifecycle != SessionLifecycle.Connected)
                return Fail<T>(AdapterErrorCodes.Disconnected, "The emulator session is disconnected.", scope.OperationId);
            if (scope.ExpectedRevision is { } expected && expected != state.StateRevision)
                return Fail<T>(AdapterErrorCodes.RevisionConflict, "The request used an old state revision.", scope.OperationId, state.StateRevision);

            var descriptor = Advertised(state, capability, subject, operation);
            if (descriptor is null || !_connection.TryGetPort<TPort>(capability, out var port))
                return Fail<T>(AdapterErrorCodes.Unsupported, "This operation is not advertised for the selected target.", scope.OperationId);

            var result = await invoke(port, descriptor).ConfigureAwait(false);
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
}
