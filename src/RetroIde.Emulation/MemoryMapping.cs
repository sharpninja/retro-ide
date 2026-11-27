namespace RetroIde.Emulation;

/// <summary>Identifies a bank, overlay, mirror, or segment and how it is selected.</summary>
/// <param name="Kind">The type of mapping.</param>
/// <param name="Id">The stable mapping identifier used by addresses.</param>
/// <param name="SelectionGroup">The group whose active mapping is reported in the address space, when selectable.</param>
/// <param name="TargetStart">The first corresponding address for a mirror or segment.</param>
/// <param name="Priority">The explicit precedence of an overlay. Ordinary mappings use zero.</param>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemoryMapping(MemoryMappingKind Kind, string Id, string? SelectionGroup = null, ulong? TargetStart = null,
    int Priority = 0);
