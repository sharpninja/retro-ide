namespace RetroIde.Emulation;

/// <summary>Contains an authoritative snapshot and the event cursor matching the snapshot.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public sealed record SessionState(
    SessionIdentity Identity,
    SessionLifecycle Lifecycle,
    ExecutionStatus Execution,
    ulong StateRevision,
    SessionClosePolicy ClosePolicy,
    CapabilitySnapshot Capabilities,
    EventCursor Cursor);
