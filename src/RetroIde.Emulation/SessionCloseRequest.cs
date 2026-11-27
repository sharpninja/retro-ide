namespace RetroIde.Emulation;

/// <summary>Names the session to release and the actor responsible for the request.</summary>
public sealed record SessionCloseRequest(SessionIdentity Session, Guid OperationId, string ActorId);
