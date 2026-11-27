namespace RetroIde.Emulation;

/// <summary>Identifies the lifecycle mode requested from an adapter.</summary>
public enum SessionMode
{
    /// <summary>Start a new host from the installed package.</summary>
    Launch,

    /// <summary>Connect to an independently owned host.</summary>
    Attach,
}
