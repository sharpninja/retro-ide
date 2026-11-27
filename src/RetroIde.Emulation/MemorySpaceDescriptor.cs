using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Describes a CPU-visible space, address units, default byte order and selected mappings.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemorySpaceDescriptor(string Id, int AddressBits, int BytesPerAddress, ByteOrder? DefaultByteOrder,
    ImmutableArray<MemoryRegionDescriptor> Regions, ImmutableDictionary<string, string> ActiveMappings);
