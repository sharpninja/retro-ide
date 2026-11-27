namespace RetroIde.Emulation;

/// <summary>Describes an inclusive address range. An absent region is unmapped.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemoryRegionDescriptor(string Id, ulong Start, ulong End, MemoryRegionKind Kind, MemoryAccess Access,
    MemoryMapping? Mapping = null);
