namespace RetroIde.Emulation;

/// <summary>Confirms that the host applied the scoped mutation at the reported state revision.</summary>
public sealed record MutationReceipt(SessionOperation Scope, ulong StateRevision);
