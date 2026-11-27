using System.Collections.Immutable;
using RetroIde.Emulation;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    private interface IFutureAdapterPort : IEmulatorPort
    {
        string Probe();
    }

    private sealed partial class ControlAdapter : ITimingPort, IRegisterPort, IDebuggerPort, IMachineDescriptorPort, IInputPort, IMediaStoragePort, IFutureAdapterPort
    {
        private static readonly ImmutableArray<CapabilityDescriptor> DebugAndDeviceCapabilities =
        [
            Advertise(ITimingPort.Capability, "cpu.clock", [ITimingPort.Observe, ITimingPort.SetThrottle, ITimingPort.ReadBus]),
            Advertise(IRegisterPort.Capability, "cpu", [IRegisterPort.Read, IRegisterPort.Write]),
            Advertise(IDebuggerPort.Capability, "cpu", [IDebuggerPort.ListRules, IDebuggerPort.ChangeRule, IDebuggerPort.Disassemble, IDebuggerPort.ReadTrace]),
            MachineDebugAdapterFixture.DescriptorCapability,
            Advertise(IInputPort.Capability, "keyboard", [IInputPort.Send, IInputPort.Release], Limit("max-events", 2)),
            Advertise(IMediaStoragePort.Capability, null, [IMediaStoragePort.ListSlots]),
            Advertise(IMediaStoragePort.Capability, "drive-0", [IMediaStoragePort.Mount, IMediaStoragePort.Eject]),
        ];

        private static readonly ArtifactReference Disk = new("fixture.disk", ArtifactReference.Origin.Uploaded);
        private static readonly ITimingPort.Clock Clock = new("cpu.clock");
        private ImmutableArray<IRegisterPort.RegisterValue> _registers = [new("D0", 32, [0, 0, 0, 1], true)];
        private decimal _rate = 1;
        private IDebuggerPort.Rule? _rule;
        private ArtifactReference? _mounted;
        private readonly Dictionary<IInputPort.Capture, ulong> _next = [];
        internal ImmutableArray<CapabilityDescriptor> AdditionalCapabilities { get; private set; } = [];
        internal List<CapabilityId> PortRequests { get; } = [];
        internal bool DropNextPortReply { get; set; }

        internal void EnableAdditionalPorts()
        {
            AdditionalCapabilities = [.. DebugAndDeviceCapabilities, .. ServiceCapabilities];
            Current = Current with { Capabilities = Current.Capabilities with { Items = Current.Capabilities.Items.AddRange(AdditionalCapabilities) } };
        }

        string IFutureAdapterPort.Probe() => "future reply";

        ValueTask<AdapterResult<ITimingPort.Observation>> ITimingPort.ObserveAsync(ScopedQuery<ITimingPort.Clock> request, CancellationToken cancellationToken) =>
            Success(new ITimingPort.Observation(Clock, new("cpu.clock", 30, 1000), 12, "phase-1", _rate));

        ValueTask<AdapterResult<MutationReceipt>> ITimingPort.SetThrottleAsync(MutationRequest<ITimingPort.Throttle> request, CancellationToken cancellationToken) =>
            ApplyPort(request.Scope, () => _rate = request.Payload.SpeedRatio ?? 1);

        ValueTask<AdapterResult<ITimingPort.BusTrace>> ITimingPort.ReadBusAsync(ScopedQuery<ITimingPort.BusRange> request, CancellationToken cancellationToken) =>
            Success(new ITimingPort.BusTrace(Clock, false, [new(3, new("cpu.clock", 30, 1000), ITimingPort.BusCycle.Read,
                new("main", 0x200, "cpu", MapRevision: 8), "phase-1", [0x4E, 0x71])]));

        ValueTask<AdapterResult<IRegisterPort.Snapshot>> IRegisterPort.ReadAsync(ScopedQuery<IRegisterPort.Selection> request, CancellationToken cancellationToken) =>
            Success(new IRegisterPort.Snapshot(request.Payload.CpuId, Current.StateRevision, _registers));

        ValueTask<AdapterResult<MutationReceipt>> IRegisterPort.WriteAsync(MutationRequest<IRegisterPort.Update> request, CancellationToken cancellationToken) =>
            ApplyPort(request.Scope, () => _registers = request.Payload.Values);

        ValueTask<AdapterResult<IDebuggerPort.RuleSet>> IDebuggerPort.ListRulesAsync(ScopedQuery<IDebuggerPort.Cpu> request, CancellationToken cancellationToken) =>
            Success(new IDebuggerPort.RuleSet(request.Payload.Id, Current.StateRevision, _rule is null ? [] : [_rule]));

        ValueTask<AdapterResult<MutationReceipt>> IDebuggerPort.ChangeRuleAsync(MutationRequest<IDebuggerPort.RuleChange> request, CancellationToken cancellationToken) =>
            ApplyPort(request.Scope, () => _rule = request.Payload.Action == IDebuggerPort.RuleAction.Put ? request.Payload.Rule : null);

        ValueTask<AdapterResult<IDebuggerPort.Decoded>> IDebuggerPort.DisassembleAsync(ScopedQuery<IDebuggerPort.DisassemblyRange> request, CancellationToken cancellationToken) =>
            Success(new IDebuggerPort.Decoded(request.Payload.Start, Current.StateRevision, [Decode(request.Payload.Start)]));

        ValueTask<AdapterResult<IDebuggerPort.Trace>> IDebuggerPort.ReadTraceAsync(ScopedQuery<IDebuggerPort.TraceRange> request, CancellationToken cancellationToken) =>
            Success(new IDebuggerPort.Trace(request.Payload.CpuId, false, [new(1, new("cpu.clock", 30, 1000),
                Decode(new("main", 0x200, request.Payload.CpuId, MapRevision: 8)))]));

        ValueTask<AdapterResult<MachineDescriptor>> IMachineDescriptorPort.DescribeAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(MachineDebugAdapterFixture.DescribeTarget(Identity, 8));

        private static IDebuggerPort.Instruction Decode(MemoryAddress address) => new(address, [0x4E, 0x71], "nop", string.Empty);

        ValueTask<AdapterResult<MutationReceipt>> IInputPort.SendAsync(MutationRequest<IInputPort.Batch> request, CancellationToken cancellationToken)
        {
            var batch = request.Payload;
            if (batch.FirstSequence == ulong.MaxValue || _next.GetValueOrDefault(batch.Target) != batch.FirstSequence)
                return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.InvalidState,
                    "Input sequence does not match this capture.", request.Scope.OperationId)));
            return ApplyPort(request.Scope, () => _next[batch.Target] = batch.FirstSequence + (ulong)batch.Events.Length);
        }

        ValueTask<AdapterResult<MutationReceipt>> IInputPort.ReleaseAsync(MutationRequest<IInputPort.Capture> request, CancellationToken cancellationToken) =>
            ApplyPort(request.Scope, () => _next.Remove(request.Payload));

        ValueTask<AdapterResult<IMediaStoragePort.Slots>> IMediaStoragePort.ListAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new IMediaStoragePort.Slots([new("drive-0", "floppy", ["adf"], _mounted, true)]));

        ValueTask<AdapterResult<MutationReceipt>> IMediaStoragePort.ChangeAsync(MutationRequest<IMediaStoragePort.Change> request, CancellationToken cancellationToken) =>
            ApplyPort(request.Scope, () => _mounted = request.Payload is IMediaStoragePort.Change.Insert insert ? insert.Artifact : null);

        private ValueTask<AdapterResult<MutationReceipt>> ApplyPort(SessionOperation scope, Action apply)
        {
            if (!string.Equals(scope.ActorId, "desktop", StringComparison.Ordinal))
                return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.PermissionDenied, "Actor denied by host.", scope.OperationId)));
            if (scope.ExpectedRevision is { } expected && expected != Current.StateRevision)
                return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.RevisionConflict,
                    "State changed at the host.", scope.OperationId, Current.StateRevision)));

            apply();
            Current = Current with { StateRevision = Current.StateRevision + 1, Cursor = new(Identity, Current.Cursor.Sequence + 1) };
            if (DropNextPortReply)
            {
                DropNextPortReply = false;
                return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.Timeout, "Host reply lost.")));
            }
            return Success(new MutationReceipt(scope, Current.StateRevision));
        }

        private static CapabilityDescriptor Advertise(CapabilityId id, string? subject, ImmutableArray<string> operations,
            ImmutableDictionary<string, ulong>? limits = null) => new(id, subject, operations, limits ?? ImmutableDictionary<string, ulong>.Empty);

        private static ImmutableDictionary<string, ulong> Limit(string name, ulong value) => ImmutableDictionary<string, ulong>.Empty.Add(name, value);

        private static ValueTask<AdapterResult<T>> Success<T>(T data) where T : class => ValueTask.FromResult(AdapterResult<T>.Success(data));

        private static ValueTask<AdapterResult<T>> Unsupported<T>() where T : class =>
            ValueTask.FromResult(AdapterResult<T>.Failure(new(AdapterErrorCodes.Unsupported, "Fixture does not advertise this operation.")));
    }
}
