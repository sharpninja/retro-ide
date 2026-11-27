using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application.Emulators;
using RetroIde.Emulation;

namespace RetroIde.IntegrationTests;

internal sealed class MachineDebugAdapterFixture : IEmulatorSession, IMachineDescriptorPort, IMemoryPort
{
    internal static CapabilityDescriptor DescriptorCapability { get; } = new(IMachineDescriptorPort.Capability,
        null, [IMachineDescriptorPort.Describe], ImmutableDictionary<string, ulong>.Empty);
    private static readonly CapabilityDescriptor MainMemory = new(IMemoryPort.Capability, "main",
        [IMemoryPort.InspectMapped, IMemoryPort.InspectRaw, IMemoryPort.WriteMapped],
        ImmutableDictionary<string, ulong>.Empty.Add("address-bits", 16).Add("max-read-bytes", 256).Add("max-write-bytes", 2));
    private static readonly CapabilityDescriptor AuxMemory = new(IMemoryPort.Capability, "aux", [IMemoryPort.InspectMapped],
        ImmutableDictionary<string, ulong>.Empty.Add("max-read-bytes", 64));

    internal MachineDebugAdapterFixture(bool advertised = true)
    {
        Machine = DescribeTarget(Identity);
        State = MakeState(1, 1, advertised);
    }

    public SessionIdentity Identity { get; } = new("fixture", "machine", "1.0.0", Guid.NewGuid());
    internal MachineDescriptor Machine { get; set; }
    internal SessionState State { get; set; }
    internal Action? BeforeRead { get; set; }
    internal Action<SessionEvent>? OnEvent { get; set; }
    internal int DescriptionRequests { get; private set; }
    internal List<MemoryReadRequest> Reads { get; } = [];
    internal List<MutationRequest<MemoryWriteRequest>> Writes { get; } = [];
    internal Dictionary<ulong, SessionEvent[]> Events { get; } = [];
    internal List<ulong> Subscriptions { get; } = [];
    internal EmulatorSessionControl OpenControl() => new(this, State, NullLogger.Instance);
    internal SessionOperation NewScope() => new(Identity, Guid.NewGuid(), "fixture-debugger", null);

    internal SessionState MakeState(ulong sequence, ulong revision, bool advertised = true) =>
        new(Identity, SessionLifecycle.Connected, ExecutionStatus.Paused, revision, SessionClosePolicy.Detach,
            new CapabilitySnapshot(1, advertised ? [DescriptorCapability, MainMemory, AuxMemory] : [MainMemory, AuxMemory]),
            new EventCursor(Identity, sequence));

    internal static MachineDescriptor DescribeTarget(SessionIdentity target, ulong mapRevision = 7, bool upperBank = false,
        bool bootOverlay = true) =>
        new(target, IMachineDescriptorPort.Capability.Version, mapRevision, "fixture-dual", ByteOrder.BigEndian,
            new DebuggerProfileReference("amiga-aga", 1),
            [
                new("cpu", "68000", "68040", "mc68040", ByteOrder.BigEndian, ["clock.cpu"], ["main"]),
                new("io-cpu", "Z80", "Z80", "z84c00", ByteOrder.LittleEndian, ["clock.io"], ["aux"]),
            ],
            [
                new("clock.cpu", 25_000_000, ["bus-rise", "bus-fall"]),
                new("clock.io", 4_000_000, []),
            ],
            [
                new("main", 16, 1, ByteOrder.BigEndian, Regions(), Select(upperBank, bootOverlay)),
                new("aux", 16, 2, ByteOrder.LittleEndian,
                    [new("io-ram", 0, 0xFFFF, MemoryRegionKind.Ram, MemoryAccess.Inspect)],
                    ImmutableDictionary<string, string>.Empty),
            ]);

    private static ImmutableArray<MemoryRegionDescriptor> Regions() =>
        [
            new("main-ram", 0, 0x7FFF, MemoryRegionKind.Ram, MemoryAccess.Inspect | MemoryAccess.Write),
            new("boot-rom", 0x0100, 0x01FF, MemoryRegionKind.Rom, MemoryAccess.Inspect,
                new(MemoryMappingKind.Overlay, "boot-rom", "boot", Priority: 1)),
            new("bank-a", 0x8000, 0x8FFF, MemoryRegionKind.Ram, MemoryAccess.Inspect,
                new(MemoryMappingKind.Bank, "lower", "window")),
            new("bank-b", 0x8000, 0x8FFF, MemoryRegionKind.Ram, MemoryAccess.Inspect,
                new(MemoryMappingKind.Bank, "upper", "window")),
            new("devices", 0xC000, 0xC0FF, MemoryRegionKind.Device, MemoryAccess.DeviceRead),
            new("ram-alias", 0xE000, 0xE0FF, MemoryRegionKind.Ram, MemoryAccess.Inspect,
                new(MemoryMappingKind.Mirror, "ram-alias", TargetStart: 0)),
            new("segment", 0xF000, 0xF0FF, MemoryRegionKind.Ram, MemoryAccess.Inspect,
                new(MemoryMappingKind.Segment, "far-segment", TargetStart: 0x1F000)),
        ];

    private static ImmutableDictionary<string, string> Select(bool upperBank, bool bootOverlay)
    {
        var mapping = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal).Add("window", upperBank ? "upper" : "lower");
        return bootOverlay ? mapping.Add("boot", "boot-rom") : mapping;
    }

    public ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(AdapterResult<SessionState>.Success(State));

    public ValueTask<AdapterResult<MachineDescriptor>> DescribeAsync(SessionOperation scope, CancellationToken cancellationToken)
    {
        DescriptionRequests++;
        return ValueTask.FromResult(AdapterResult<MachineDescriptor>.Success(Machine));
    }

    public ValueTask<AdapterResult<MemoryBlock>> ReadAsync(MemoryReadRequest request, CancellationToken cancellationToken)
    {
        Reads.Add(request);
        BeforeRead?.Invoke();
        if (request.Address.MapRevision is { } expected && expected != Machine.MapRevision)
            return ValueTask.FromResult(AdapterResult<MemoryBlock>.Failure(new AdapterError(AdapterErrorCodes.RevisionConflict,
                "Map changed at the host.", request.Scope.OperationId) { CurrentMapRevision = Machine.MapRevision }));

        var data = ImmutableArray.CreateRange(Enumerable.Repeat((byte)0xA5, request.ByteCount));
        return ValueTask.FromResult(AdapterResult<MemoryBlock>.Success(new(request.Scope, request.Address, State.StateRevision, data)));
    }

    public ValueTask<AdapterResult<MutationReceipt>> WriteAsync(MutationRequest<MemoryWriteRequest> request, CancellationToken cancellationToken)
    {
        if (request.Payload.Address.MapRevision != Machine.MapRevision || request.Scope.ExpectedRevision != State.StateRevision)
            return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.RevisionConflict,
                "The fixture state or map changed.", request.Scope.OperationId)));
        Writes.Add(request);
        State = State with { StateRevision = State.StateRevision + 1, Cursor = new(Identity, State.Cursor.Sequence + 1) };
        return ValueTask.FromResult(AdapterResult<MutationReceipt>.Success(new(request.Scope, State.StateRevision)));
    }

    public IAsyncEnumerable<SessionEvent> ReadEventsAsync(EventCursor? after, CancellationToken cancellationToken)
    {
        var sequence = after?.Sequence ?? 0;
        Subscriptions.Add(sequence);
        return (Events.GetValueOrDefault(sequence) ?? []).Select(item =>
        {
            OnEvent?.Invoke(item);
            return item;
        }).ToAsyncEnumerable();
    }

    public bool TryGetPort<TPort>(CapabilityId capability, [NotNullWhen(true)] out TPort? port) where TPort : class, IEmulatorPort
    {
        port = capability == IMachineDescriptorPort.Capability || capability == IMemoryPort.Capability ? this as TPort : null;
        return port is not null;
    }

    public ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(SessionCloseRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AdapterResult<SessionCloseResult>.Success(new(request.OperationId, SessionClosePolicy.Detach)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
