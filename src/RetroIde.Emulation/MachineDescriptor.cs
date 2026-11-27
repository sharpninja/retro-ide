using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Describes the exact CPUs, clock domains and active memory layout of a live session.</summary>
public sealed record MachineDescriptor(
    SessionIdentity Session, Version Version, ulong MapRevision, string MachineId, ByteOrder? DefaultByteOrder,
    DebuggerProfileReference? Profile, ImmutableArray<CpuDescriptor> Cpus,
    ImmutableArray<ClockDomainDescriptor> Clocks, ImmutableArray<MemorySpaceDescriptor> MemorySpaces);
