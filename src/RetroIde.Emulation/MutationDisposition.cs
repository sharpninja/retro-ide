namespace RetroIde.Emulation;

/// <summary>Identifies what the host can establish about a previous mutation.</summary>
public enum MutationDisposition
{
    /// <summary>The host cannot establish whether the operation was applied.</summary>
    Unknown,

    /// <summary>The host verifies that the operation was applied.</summary>
    Applied,

    /// <summary>The host verifies that the operation did not apply and will not execute later.</summary>
    NotApplied,
}
