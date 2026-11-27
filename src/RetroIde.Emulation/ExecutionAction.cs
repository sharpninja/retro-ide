namespace RetroIde.Emulation;

/// <summary>Identifies the distinct operations advertised by an execution capability.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public enum ExecutionAction
{
    /// <summary>Resume guest execution.</summary>
    Run,
    /// <summary>Suspend guest execution.</summary>
    Pause,
    /// <summary>Advance by an advertised number of instructions.</summary>
    StepInstruction,
    /// <summary>Advance by an advertised number of cycles.</summary>
    StepCycle,
    /// <summary>Advance by an advertised number of clock phases.</summary>
    StepPhase,
    /// <summary>Reset the guest.</summary>
    Reset,
    /// <summary>Power off the guest.</summary>
    PowerOff,
}
