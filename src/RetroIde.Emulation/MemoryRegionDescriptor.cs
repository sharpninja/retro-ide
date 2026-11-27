namespace RetroIde.Emulation;

/// <summary>Describes an inclusive address range. An absent region is unmapped.</summary>
public sealed record MemoryRegionDescriptor(string Id, ulong Start, ulong End, MemoryRegionKind Kind, MemoryAccess Access,
    MemoryMapping? Mapping = null);
