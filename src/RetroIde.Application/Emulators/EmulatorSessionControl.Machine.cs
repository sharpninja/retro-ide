using System.Runtime.CompilerServices;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    private const int MaximumInspectionBytes = 64 * 1024;
    private ulong _mapRevision;

    internal bool IsCurrentMap(ulong revision) => revision == Volatile.Read(ref _mapRevision);

    internal async ValueTask<AdapterResult<MachineDescriptor>> DescribeMachineAsync(SessionOperation scope, CancellationToken token) =>
        (await ObserveMachineAsync(scope, token).ConfigureAwait(false)).Result;

    private async ValueTask<(AdapterResult<MachineDescriptor> Result, SessionState Observed)> ObserveMachineAsync(
        SessionOperation scope, CancellationToken token)
    {
        while (true)
        {
            var observed = State;
            var superseded = false;
            var result = await InvokePortAsync<IMachineDescriptorPort, MachineDescriptor>(scope, IMachineDescriptorPort.Capability,
                subject: null, IMachineDescriptorPort.Describe, async selection =>
                {
                    var reply = await selection.Port.DescribeAsync(scope, token).ConfigureAwait(false);
                    observed = State;
                    if (CheckPortState(observed, scope) is { } invalid)
                        return AdapterResult<MachineDescriptor>.Failure(invalid);
                    if (!CanDescribe(observed))
                        return Fail<MachineDescriptor>(AdapterErrorCodes.Unsupported,
                            "The machine description is no longer advertised.", scope.OperationId);
                    superseded = !SameRoutingState(selection.State, observed);
                    return superseded
                        ? Fail<MachineDescriptor>(AdapterErrorCodes.RevisionConflict, "The emulator state changed during machine discovery.",
                            scope.OperationId, observed.StateRevision)
                        : ValidateMachineReply(reply, scope);
                }, token).ConfigureAwait(false);
            if (!superseded)
                return (result, observed);
        }
    }

    private AdapterResult<MachineDescriptor> ValidateMachineReply(AdapterResult<MachineDescriptor> reply, SessionOperation scope)
    {
        if (!reply.Succeeded)
            return reply;
        var machine = reply.Value;
        if (machine.Version != IMachineDescriptorPort.Capability.Version || machine.Session != _identity || !MachineMap.IsValid(machine))
            return Fail<MachineDescriptor>(AdapterErrorCodes.Incompatible, "The emulator's machine description is incompatible.", scope.OperationId);
        return ObserveMapRevision(machine.MapRevision) ? reply
            : Fail<MachineDescriptor>(AdapterErrorCodes.Incompatible, "The emulator returned an older memory map.", scope.OperationId);
    }

    internal async IAsyncEnumerable<AdapterResult<MachineDescriptor>> WatchMachineAsync(SessionOperation scope, EventCursor? after,
        [EnumeratorCancellation] CancellationToken token)
    {
        var cursor = after ?? State.Cursor;
        if (ValidateScope(scope) is { } invalid)
        {
            yield return AdapterResult<MachineDescriptor>.Failure(invalid);
            yield break;
        }
        if (cursor.Session != _identity)
        {
            yield return Fail<MachineDescriptor>(AdapterErrorCodes.Incompatible, "The cursor belongs to a different emulator session.", scope.OperationId);
            yield break;
        }

        var (initial, previous) = await ObserveMachineAsync(scope, token).ConfigureAwait(false);
        yield return initial;
        var gap = false;
        await foreach (var item in WatchAsync(cursor, token).ConfigureAwait(false))
        {
            if (!item.Succeeded)
            {
                yield return AdapterResult<MachineDescriptor>.Failure(item.Error with { OperationId = scope.OperationId });
                yield break;
            }
            switch (item.Value)
            {
                case SessionEventGap:
                    gap = true;
                    break;
                case MachineMapChanged:
                    var (mapped, mappedState) = await ObserveMachineAsync(RefreshScope(scope), token).ConfigureAwait(false);
                    previous = mappedState;
                    yield return mapped;
                    break;
                case SessionStateChanged:
                    var current = State;
                    var refresh = gap || current.Lifecycle != previous.Lifecycle || CanDescribe(current) != CanDescribe(previous) ||
                        (CanDescribe(current) && current.Capabilities.Revision != previous.Capabilities.Revision);
                    gap = false;
                    if (refresh)
                    {
                        var (description, describedState) = await ObserveMachineAsync(RefreshScope(scope), token).ConfigureAwait(false);
                        previous = describedState;
                        yield return description;
                    }
                    else
                    {
                        previous = current;
                    }
                    break;
            }
        }
    }

    private static bool CanDescribe(SessionState state) =>
        Advertised(state, IMachineDescriptorPort.Capability, subject: null, IMachineDescriptorPort.Describe) is not null;

    private static SessionOperation RefreshScope(SessionOperation scope) =>
        scope with { OperationId = Guid.NewGuid(), ExpectedRevision = null };

    internal ValueTask<AdapterResult<MemoryBlock>> ReadMemoryAsync(MemoryReadRequest request, CancellationToken token)
    {
        var address = request.Address;
        var mapped = address.MapRevision.HasValue;
        if (!MachineMap.Identifier(address.SpaceId) || request.ByteCount is <= 0 or > MaximumInspectionBytes ||
            (!mapped && (address.CpuId is not null || address.MappingId is not null)))
            return ValueTask.FromResult(Fail<MemoryBlock>(AdapterErrorCodes.InvalidArgument, "The memory read is invalid.", request.Scope.OperationId));

        return InvokePortAsync<IMemoryPort, MemoryBlock>(request.Scope, IMemoryPort.Capability, address.SpaceId,
            mapped ? IMemoryPort.InspectMapped : IMemoryPort.InspectRaw, async (port, _) =>
            {
                var result = await port.ReadAsync(request, token).ConfigureAwait(false);
                if (!result.Succeeded)
                    return result;
                var block = result.Value;
                return block.Scope == request.Scope && block.Address == address &&
                    !block.Data.IsDefault && block.Data.Length == request.ByteCount &&
                    (request.Scope.ExpectedRevision is not { } expected || block.StateRevision == expected)
                    ? result : Fail<MemoryBlock>(AdapterErrorCodes.Incompatible, "The emulator returned bytes for an incompatible request.",
                        request.Scope.OperationId);
            }, token, (descriptor, cancellation) => CheckMemoryRequestAsync(request, descriptor, cancellation));
    }

    private async ValueTask<AdapterError?> CheckMemoryRequestAsync(MemoryReadRequest request, CapabilityDescriptor descriptor, CancellationToken token)
    {
        var id = request.Scope.OperationId;
        if (!descriptor.Limits.TryGetValue("max-read-bytes", out var limit) || (ulong)request.ByteCount > limit)
            return new(AdapterErrorCodes.InvalidArgument, "The read exceeds the advertised byte limit.", id);
        if (request.Address.MapRevision is null)
        {
            var width = descriptor.Limits.GetValueOrDefault("address-bits");
            var units = descriptor.Limits.GetValueOrDefault("bytes-per-address", 1UL);
            return width is >= 1 and <= 64 && units is >= 1 and <= 64 &&
                MachineMap.TryEndAddress(request.Address.Offset, request.ByteCount, (int)width, (int)units, out _)
                ? null : new(AdapterErrorCodes.InvalidArgument, "The raw read exceeds the advertised address range.", id);
        }

        var machine = await DescribeMachineAsync(RefreshScope(request.Scope), token).ConfigureAwait(false);
        return machine.Succeeded ? MachineMap.CheckMappedAccess(machine.Value, request.Scope, request.Address, request.ByteCount, MemoryAccess.Inspect)
            : machine.Error with { OperationId = id };
    }

    internal ValueTask<AdapterResult<MutationReceipt>> WriteMemoryAsync(MutationRequest<MemoryWriteRequest> request, CancellationToken token)
    {
        var address = request.Payload.Address;
        if (!MachineMap.Identifier(address.SpaceId) || address.MapRevision is null || request.Payload.Data.IsDefaultOrEmpty ||
            request.Payload.Data.Length > MaximumInspectionBytes)
            return InvalidPortInput<MutationReceipt>(request.Scope);
        var bytes = request.Payload.Data.Length;

        return MutatePortAsync<IMemoryPort>(new(request.Scope, IMemoryPort.Capability, address.SpaceId, IMemoryPort.WriteMapped, request.Payload),
            (port, cancellation) => port.WriteAsync(request, cancellation), async (descriptor, cancellation) =>
            {
                if (!descriptor.Limits.TryGetValue("max-write-bytes", out var max) || (ulong)bytes > max)
                    return PortLimit(request.Scope, "write size");
                var machine = await DescribeMachineAsync(RefreshScope(request.Scope), cancellation).ConfigureAwait(false);
                return machine.Succeeded
                    ? MachineMap.CheckMappedAccess(machine.Value, request.Scope, address, bytes, MemoryAccess.Write)
                    : machine.Error with { OperationId = request.Scope.OperationId };
            }, token);
    }

    private bool ObserveMapRevision(ulong revision)
    {
        while (true)
        {
            var current = Volatile.Read(ref _mapRevision);
            if (revision < current)
                return false;
            if (Interlocked.CompareExchange(ref _mapRevision, revision, current) == current)
                return true;
        }
    }
}
