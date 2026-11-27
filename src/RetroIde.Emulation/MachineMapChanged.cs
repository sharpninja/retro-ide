namespace RetroIde.Emulation;

/// <summary>Signals that clients must refresh the selected session's active debugger map.</summary>
public sealed record MachineMapChanged(EventCursor Cursor, ulong StateRevision, ulong MapRevision) : SessionEvent(Cursor, StateRevision);
