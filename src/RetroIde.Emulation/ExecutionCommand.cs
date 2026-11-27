namespace RetroIde.Emulation;

/// <summary>Requests an execution action on the session or an advertised CPU subject.</summary>
public sealed record ExecutionCommand(ExecutionAction Action, string? SubjectId = null, uint Count = 1)
{
    /// <summary>Gets the capability operation identifier for the selected execution action.</summary>
    public string Operation => Action switch
    {
        ExecutionAction.Run => "run",
        ExecutionAction.Pause => "pause",
        ExecutionAction.StepInstruction => "step-instruction",
        ExecutionAction.StepCycle => "step-cycle",
        ExecutionAction.StepPhase => "step-phase",
        ExecutionAction.Reset => "reset",
        ExecutionAction.PowerOff => "power-off",
        _ => throw new InvalidOperationException($"Unknown execution action {Action}."),
    };
}
