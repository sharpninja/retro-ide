namespace RetroIde.Emulation;

/// <summary>Identifies how a region is mapped into a CPU address space.</summary>
public enum MemoryMappingKind
{
    /// <summary>Selects one bank in a reported group.</summary>
    Bank,
    /// <summary>Places a higher-priority range over another mapping.</summary>
    Overlay,
    /// <summary>Aliases an existing direct range.</summary>
    Mirror,
    /// <summary>Translates a segment-relative range to the reported address base.</summary>
    Segment,
}
