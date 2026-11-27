namespace RetroIde.Emulation;

/// <summary>Identifies an address expressed in the selected space's address units.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemoryAddress(string SpaceId, ulong Offset, string? CpuId = null, string? MappingId = null, ulong? MapRevision = null);
