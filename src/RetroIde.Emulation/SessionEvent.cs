namespace RetroIde.Emulation;

/// <summary>Represents a control event; optional event types can extend the model independently.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public abstract record SessionEvent(EventCursor Cursor, ulong StateRevision);
