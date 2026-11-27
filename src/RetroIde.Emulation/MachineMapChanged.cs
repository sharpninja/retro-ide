namespace RetroIde.Emulation;

/// <summary>Signals that clients must refresh the selected session's active debugger map.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MachineMapChanged(EventCursor Cursor, ulong StateRevision, ulong MapRevision) : SessionEvent(Cursor, StateRevision);
