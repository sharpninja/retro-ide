namespace RetroIde.Emulation;

/// <summary>Shares the new authoritative state after a lifecycle, execution or capability change.</summary>
public sealed record SessionStateChanged(SessionState State) : SessionEvent(State.Cursor, State.StateRevision);
