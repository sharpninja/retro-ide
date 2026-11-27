namespace RetroIde.Emulation;

/// <summary>Identifies the policy established at session open.</summary>
public enum SessionClosePolicy
{
    /// <summary>Release this IDE connection while leaving the independently owned emulator running.</summary>
    Detach,

    /// <summary>Stop the host that the IDE launched and owns, and release the connection.</summary>
    StopOwnedHost,
}
