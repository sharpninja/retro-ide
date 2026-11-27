namespace RetroIde.Emulation;

/// <summary>Identifies a control-event position scoped to one emulator instance.</summary>
public sealed record EventCursor(SessionIdentity Session, ulong Sequence);
