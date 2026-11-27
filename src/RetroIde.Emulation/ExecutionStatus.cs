namespace RetroIde.Emulation;

/// <summary>Identifies the currently observed guest execution state.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public enum ExecutionStatus
{
    /// <summary>The backend has not reported an execution state.</summary>
    Unknown,

    /// <summary>Guest instructions are executing.</summary>
    Running,

    /// <summary>Guest execution is suspended.</summary>
    Paused,

    /// <summary>The guest is stopped.</summary>
    Stopped,
}
