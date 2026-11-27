namespace RetroIde.Emulation;

/// <summary>Represents a control event; optional event types can extend the model independently.</summary>
public abstract record SessionEvent(EventCursor Cursor, ulong StateRevision);
