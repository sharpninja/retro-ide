namespace RetroIde.Emulation;

/// <summary>Reports a nonconsecutive event history and requires the client to reload authoritative state.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record SessionEventGap(EventCursor Cursor, ulong StateRevision) : SessionEvent(Cursor, StateRevision);
