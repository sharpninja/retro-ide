namespace RetroIde.Emulation;

/// <summary>Contains an authoritative snapshot and the event cursor matching the snapshot.</summary>
public sealed record SessionState(
    SessionIdentity Identity,
    SessionLifecycle Lifecycle,
    ExecutionStatus Execution,
    ulong StateRevision,
    SessionClosePolicy ClosePolicy,
    CapabilitySnapshot Capabilities,
    EventCursor Cursor);
