namespace RetroIde.Emulation;

/// <summary>A stable error code, optional operation correlation and relevant current revision.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record AdapterError(string Code, string Message, Guid? OperationId = null, ulong? CurrentRevision = null)
{
    /// <summary>Gets the current map revision when a mapped inspection precondition failed.</summary>
    public ulong? CurrentMapRevision { get; init; }
}
