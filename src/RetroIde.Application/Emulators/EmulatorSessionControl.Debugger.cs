using System.Collections.Immutable;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal ValueTask<AdapterResult<ITimingPort.Observation>> ObserveTimingAsync(ScopedQuery<ITimingPort.Clock> request, CancellationToken token)
    {
        var id = request.Payload.Id;
        return MachineMap.Identifier(id)
            ? InvokePortAsync<ITimingPort, ITimingPort.Observation>(request.Scope, ITimingPort.Capability, id, ITimingPort.Observe,
                async (port, _) =>
                {
                    var result = await port.ObserveAsync(request, token).ConfigureAwait(false);
                    if (!result.Succeeded)
                        return result;
                    return result.Value.Clock == request.Payload && ValidMediaTime(result.Value.SampledAt)
                        ? result : Fail<ITimingPort.Observation>(AdapterErrorCodes.Incompatible,
                            "The emulator returned an invalid clock observation.", request.Scope.OperationId);
                }, token)
            : InvalidPortInput<ITimingPort.Observation>(request.Scope);
    }

    internal ValueTask<AdapterResult<MutationReceipt>> SetThrottleAsync(MutationRequest<ITimingPort.Throttle> request, CancellationToken token)
    {
        var change = request.Payload;
        return MachineMap.Identifier(change.Clock.Id) && MachineMap.Identifier(change.ModeId) && change.SpeedRatio is not <= 0
            ? MutatePortAsync<ITimingPort>(new(request.Scope, ITimingPort.Capability, change.Clock.Id, ITimingPort.SetThrottle, change),
                (port, cancellation) => port.SetThrottleAsync(request, cancellation), null, token)
            : InvalidPortInput<MutationReceipt>(request.Scope);
    }

    internal ValueTask<AdapterResult<ITimingPort.BusTrace>> ReadBusAsync(ScopedQuery<ITimingPort.BusRange> request, CancellationToken token)
    {
        var range = request.Payload;
        return MachineMap.Identifier(range.Clock.Id) && range.Limit is > 0 and <= 1024
            ? InvokePortAsync<ITimingPort, ITimingPort.BusTrace>(request.Scope, ITimingPort.Capability, range.Clock.Id, ITimingPort.ReadBus,
                async (port, _) =>
                {
                    var result = await port.ReadBusAsync(request, token).ConfigureAwait(false);
                    if (!result.Succeeded)
                        return result;
                    var data = result.Value;
                    var valid = data.Clock == range.Clock && !data.Items.IsDefault && data.Items.Length <= range.Limit &&
                        AllOrdered(data.Items, range.After, static entry => entry.Sequence, static entry =>
                            ValidMediaTime(entry.Time) && Enum.IsDefined(entry.Cycle) && !entry.Data.IsDefault && entry.Data.Length <= 64);
                    return valid ? result : Fail<ITimingPort.BusTrace>(AdapterErrorCodes.Incompatible,
                        "The emulator returned invalid bus observations.", request.Scope.OperationId);
                }, token)
            : InvalidPortInput<ITimingPort.BusTrace>(request.Scope);
    }

    internal ValueTask<AdapterResult<IRegisterPort.Snapshot>> ReadRegistersAsync(ScopedQuery<IRegisterPort.Selection> request, CancellationToken token)
    {
        var selection = request.Payload;
        if (!MachineMap.Identifier(selection.CpuId) || selection.RegisterIds.IsDefault || selection.RegisterIds.Length > 256 ||
            !selection.RegisterIds.All(MachineMap.Identifier) || selection.RegisterIds.Distinct(StringComparer.Ordinal).Count() != selection.RegisterIds.Length)
            return InvalidPortInput<IRegisterPort.Snapshot>(request.Scope);

        return InvokePortAsync<IRegisterPort, IRegisterPort.Snapshot>(request.Scope, IRegisterPort.Capability, selection.CpuId, IRegisterPort.Read,
            async (port, _) =>
            {
                var result = await port.ReadAsync(request, token).ConfigureAwait(false);
                if (!result.Succeeded)
                    return result;
                var values = result.Value;
                return string.Equals(values.CpuId, selection.CpuId, StringComparison.Ordinal) &&
                    (request.Scope.ExpectedRevision is not { } expected || values.StateRevision == expected) &&
                    ValidRegisters(values.Values, allowEmpty: true) &&
                    (selection.RegisterIds.IsEmpty || values.Values.Select(static value => value.Id).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(selection.RegisterIds))
                    ? result : Fail<IRegisterPort.Snapshot>(AdapterErrorCodes.Incompatible, "The emulator returned an invalid register snapshot.", request.Scope.OperationId);
            }, token);
    }

    internal ValueTask<AdapterResult<MutationReceipt>> WriteRegistersAsync(MutationRequest<IRegisterPort.Update> request, CancellationToken token)
    {
        var update = request.Payload;
        return MachineMap.Identifier(update.CpuId) && ValidRegisters(update.Values, allowEmpty: false)
            ? MutatePortAsync<IRegisterPort>(new(request.Scope, IRegisterPort.Capability, update.CpuId, IRegisterPort.Write, update),
                (port, cancellation) => port.WriteAsync(request, cancellation), null, token)
            : InvalidPortInput<MutationReceipt>(request.Scope);
    }

    internal ValueTask<AdapterResult<IDebuggerPort.RuleSet>> ListStopRulesAsync(ScopedQuery<IDebuggerPort.Cpu> request, CancellationToken token) =>
        MachineMap.Identifier(request.Payload.Id)
            ? InvokePortAsync<IDebuggerPort, IDebuggerPort.RuleSet>(request.Scope, IDebuggerPort.Capability, request.Payload.Id,
                IDebuggerPort.ListRules, (port, _) => port.ListRulesAsync(request, token), token)
            : InvalidPortInput<IDebuggerPort.RuleSet>(request.Scope);

    internal ValueTask<AdapterResult<MutationReceipt>> ChangeStopRuleAsync(MutationRequest<IDebuggerPort.RuleChange> request, CancellationToken token)
    {
        var rule = request.Payload.Rule;
        var knownTriggers = IDebuggerPort.Trigger.Execute | IDebuggerPort.Trigger.Read | IDebuggerPort.Trigger.Write;
        if (!Enum.IsDefined(request.Payload.Action) || !MachineMap.Identifier(rule.Id) || !MachineMap.Identifier(rule.Address.CpuId) ||
            !MachineMap.Identifier(rule.Address.SpaceId) || rule.Address.MapRevision is null ||
            rule.ByteCount is <= 0 or > MaximumInspectionBytes || rule.When == IDebuggerPort.Trigger.None ||
            (rule.When & ~knownTriggers) != IDebuggerPort.Trigger.None)
            return InvalidPortInput<MutationReceipt>(request.Scope);

        return MutatePortAsync<IDebuggerPort>(new(request.Scope, IDebuggerPort.Capability, rule.Address.CpuId, IDebuggerPort.ChangeRule, request.Payload),
            (port, cancellation) => port.ChangeRuleAsync(request, cancellation),
            request.Payload.Action == IDebuggerPort.RuleAction.Put
                ? (_, cancellation) => ValidateStopAddressAsync(request.Scope, rule, cancellation) : null, token);
    }

    internal ValueTask<AdapterResult<IDebuggerPort.Decoded>> DisassembleAsync(ScopedQuery<IDebuggerPort.DisassemblyRange> request, CancellationToken token)
    {
        var range = request.Payload;
        MachineDescriptor? checkedMap = null;
        return MachineMap.Identifier(range.Start.CpuId) && MachineMap.Identifier(range.Start.SpaceId) &&
            range.Start.MapRevision.HasValue && range.InstructionCount is > 0 and <= 256
            ? InvokePortAsync<IDebuggerPort, IDebuggerPort.Decoded>(request.Scope, IDebuggerPort.Capability, range.Start.CpuId,
                IDebuggerPort.Disassemble, (port, _) => CheckedDisassemblyAsync(port, request, checkedMap!, token), token,
                async (_, cancellation) =>
                {
                    var map = await DescribeMachineAsync(RefreshScope(request.Scope), cancellation).ConfigureAwait(false);
                    if (!map.Succeeded)
                        return map.Error with { OperationId = request.Scope.OperationId };
                    checkedMap = map.Value;
                    return MachineMap.CheckMappedAccess(map.Value, request.Scope, range.Start, DisassemblyUnit(map.Value, range.Start),
                        MemoryAccess.Inspect);
                })
            : InvalidPortInput<IDebuggerPort.Decoded>(request.Scope);
    }

    private async ValueTask<AdapterError?> ValidateStopAddressAsync(SessionOperation scope, IDebuggerPort.Rule rule, CancellationToken token)
    {
        var machine = await DescribeMachineAsync(RefreshScope(scope), token).ConfigureAwait(false);
        return machine.Succeeded
            ? MachineMap.CheckMappedAccess(machine.Value, scope, rule.Address, rule.ByteCount, MemoryAccess.None)
            : machine.Error with { OperationId = scope.OperationId };
    }

    private async ValueTask<AdapterResult<IDebuggerPort.Decoded>> CheckedDisassemblyAsync(IDebuggerPort port,
        ScopedQuery<IDebuggerPort.DisassemblyRange> request, MachineDescriptor map, CancellationToken token)
    {
        var scope = request.Scope;
        var range = request.Payload;
        var unit = DisassemblyUnit(map, range.Start);

        var result = await port.DisassembleAsync(request, token).ConfigureAwait(false);
        if (!result.Succeeded)
            return result;
        var reply = result.Value;
        if (reply.Start != range.Start || reply.Items.IsDefault || reply.Items.Length > range.InstructionCount ||
            (scope.ExpectedRevision is { } expected && reply.StateRevision != expected))
            return Fail<IDebuggerPort.Decoded>(AdapterErrorCodes.Incompatible, "The emulator returned an invalid disassembly range.", scope.OperationId);

        ulong? previousEnd = null;
        foreach (var item in reply.Items)
        {
            if (item.Bytes.IsDefaultOrEmpty || item.Bytes.Length > 256 || !SameSpace(item.Address, range.Start) ||
                item.Address.Offset < range.Start.Offset || item.Address.Offset <= previousEnd ||
                MachineMap.CheckMappedAccess(map, scope, item.Address, item.Bytes.Length, MemoryAccess.Inspect) is not null)
                return Fail<IDebuggerPort.Decoded>(AdapterErrorCodes.Incompatible,
                    "The emulator returned instructions outside the selected mapping.", scope.OperationId);
            previousEnd = item.Address.Offset + (ulong)(item.Bytes.Length / unit - 1);
        }
        return result;
    }

    private static int DisassemblyUnit(MachineDescriptor machine, MemoryAddress start) =>
        machine.MemorySpaces.FirstOrDefault(space => string.Equals(space.Id, start.SpaceId, StringComparison.Ordinal))?.BytesPerAddress ?? 1;

    private static bool SameSpace(MemoryAddress address, MemoryAddress start) =>
        address.MapRevision == start.MapRevision && string.Equals(address.CpuId, start.CpuId, StringComparison.Ordinal) &&
        string.Equals(address.SpaceId, start.SpaceId, StringComparison.Ordinal) &&
        (start.MappingId is null || string.Equals(address.MappingId, start.MappingId, StringComparison.Ordinal));

    internal ValueTask<AdapterResult<IDebuggerPort.Trace>> ReadTraceAsync(ScopedQuery<IDebuggerPort.TraceRange> request, CancellationToken token) =>
        MachineMap.Identifier(request.Payload.CpuId) && request.Payload.Limit is > 0 and <= 1024
            ? InvokePortAsync<IDebuggerPort, IDebuggerPort.Trace>(request.Scope, IDebuggerPort.Capability, request.Payload.CpuId,
                IDebuggerPort.ReadTrace, async (port, _) =>
                {
                    var result = await port.ReadTraceAsync(request, token).ConfigureAwait(false);
                    if (!result.Succeeded)
                        return result;
                    var trace = result.Value;
                    var valid = string.Equals(trace.CpuId, request.Payload.CpuId, StringComparison.Ordinal) &&
                        !trace.Entries.IsDefault && trace.Entries.Length <= request.Payload.Limit &&
                        AllOrdered(trace.Entries, request.Payload.After, static entry => entry.Sequence, entry =>
                            string.Equals(entry.Instruction.Address.CpuId, trace.CpuId, StringComparison.Ordinal) && ValidMediaTime(entry.Timestamp) &&
                            !entry.Instruction.Bytes.IsDefaultOrEmpty && entry.Instruction.Bytes.Length <= 256);
                    return valid ? result : Fail<IDebuggerPort.Trace>(AdapterErrorCodes.Incompatible,
                        "The emulator returned invalid trace entries.", request.Scope.OperationId);
                }, token)
            : InvalidPortInput<IDebuggerPort.Trace>(request.Scope);

    private static bool AllOrdered<T>(ImmutableArray<T> items, ulong? previous, Func<T, ulong> sequence, Func<T, bool> valid) where T : class
    {
        foreach (var item in items)
        {
            if (item is null || !valid(item))
                return false;
            var position = sequence(item);
            if (position <= previous)
                return false;
            previous = position;
        }
        return true;
    }

    private static bool ValidRegisters(ImmutableArray<IRegisterPort.RegisterValue> values, bool allowEmpty)
    {
        if (values.IsDefault || values.Length > 256 || (!allowEmpty && values.IsEmpty))
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => value is not null && MachineMap.Identifier(value.Id) && ids.Add(value.Id) &&
            value.WidthBits is > 0 and <= 1024 && !value.Value.IsDefault && value.Value.Length == (value.WidthBits + 7) / 8);
    }

    private static ValueTask<AdapterResult<T>> InvalidPortInput<T>(SessionOperation scope) where T : class =>
        ValueTask.FromResult(Fail<T>(AdapterErrorCodes.InvalidArgument, "The emulator operation arguments are invalid.", scope.OperationId));

    private static AdapterError PortLimit(SessionOperation scope, string description) =>
        new(AdapterErrorCodes.InvalidArgument, $"The emulator's advertised {description} limit would be exceeded.", scope.OperationId);
}
