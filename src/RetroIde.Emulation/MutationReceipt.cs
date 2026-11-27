namespace RetroIde.Emulation;

/// <summary>Confirms that the host applied the scoped mutation at the reported state revision.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record MutationReceipt(SessionOperation Scope, ulong StateRevision);
