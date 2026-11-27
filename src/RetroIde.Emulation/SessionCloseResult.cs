namespace RetroIde.Emulation;

/// <summary>Reports the result of releasing the connection.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public sealed record SessionCloseResult(Guid OperationId, SessionClosePolicy AppliedPolicy);
