namespace RetroIde.Emulation;

/// <summary>Names the session to release and the actor responsible for the request.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public sealed record SessionCloseRequest(SessionIdentity Session, Guid OperationId, string ActorId);
