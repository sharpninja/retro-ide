namespace RetroIde.Emulation;

/// <summary>Identifies one actor's operation against an exact session and optional state precondition.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record SessionOperation(SessionIdentity Session, Guid OperationId, string ActorId, ulong? ExpectedRevision);
