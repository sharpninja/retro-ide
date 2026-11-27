namespace RetroIde.Emulation;

/// <summary>Identifies an address expressed in the selected space's address units.</summary>
public sealed record MemoryAddress(string SpaceId, ulong Offset, string? CpuId = null, string? MappingId = null, ulong? MapRevision = null);
