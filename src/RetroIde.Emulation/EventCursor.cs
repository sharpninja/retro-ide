namespace RetroIde.Emulation;

/// <summary>Identifies a control-event position scoped to one emulator instance.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record EventCursor(SessionIdentity Session, ulong Sequence);
