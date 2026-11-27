namespace RetroIde.Emulation;

/// <summary>Shares the new authoritative state after a lifecycle, execution or capability change.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record SessionStateChanged(SessionState State) : SessionEvent(State.Cursor, State.StateRevision);
