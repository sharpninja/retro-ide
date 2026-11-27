namespace RetroIde.Emulation;

/// <summary>Identifies the nature of a mapped memory region.</summary>
public enum MemoryRegionKind
{
    /// <summary>Volatile or writable guest memory.</summary>
    Ram,
    /// <summary>Read-only guest memory.</summary>
    Rom,
    /// <summary>Memory-mapped device registers or buffers.</summary>
    Device,
}
