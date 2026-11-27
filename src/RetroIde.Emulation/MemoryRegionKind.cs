namespace RetroIde.Emulation;

/// <summary>Identifies the nature of a mapped memory region.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public enum MemoryRegionKind
{
    /// <summary>Volatile or writable guest memory.</summary>
    Ram,
    /// <summary>Read-only guest memory.</summary>
    Rom,
    /// <summary>Memory-mapped device registers or buffers.</summary>
    Device,
}
