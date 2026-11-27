namespace RetroIde.Emulation;

/// <summary>Identifies the independently supported accesses to a memory region.</summary>
[Flags]
public enum MemoryAccess
{
    /// <summary>The region is present without any supported debugger accesses.</summary>
    None = 0,
    /// <summary>A debugger may read the region without side effects.</summary>
    Inspect = 1,
    /// <summary>A read can invoke device behavior.</summary>
    DeviceRead = 2,
    /// <summary>A debugger may request a write.</summary>
    Write = 4,
}
