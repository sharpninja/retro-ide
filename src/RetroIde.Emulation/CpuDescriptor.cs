using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Identifies a CPU instance and the exact model and variant needed to decode its instructions.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record CpuDescriptor(string Id, string Family, string Model, string Variant, ByteOrder? DefaultByteOrder,
    ImmutableArray<string> ClockDomainIds, ImmutableArray<string> MemorySpaceIds);
