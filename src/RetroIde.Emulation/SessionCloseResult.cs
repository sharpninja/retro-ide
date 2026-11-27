namespace RetroIde.Emulation;

/// <summary>Reports the result of releasing the connection.</summary>
public sealed record SessionCloseResult(Guid OperationId, SessionClosePolicy AppliedPolicy);
