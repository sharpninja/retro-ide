namespace RetroIde.Emulation;

/// <summary>Identifies one actor's operation against an exact session and optional state precondition.</summary>
public sealed record SessionOperation(SessionIdentity Session, Guid OperationId, string ActorId, ulong? ExpectedRevision);
