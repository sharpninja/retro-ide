namespace RetroIde.Emulation;

/// <summary>Reports a debugger stop and correlates it with the CPU and optional stop rule.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record DebuggerStopped(EventCursor Cursor, ulong StateRevision, string CpuId, string Reason,
    MemoryAddress? Address = null, string? RuleId = null) : SessionEvent(Cursor, StateRevision);
