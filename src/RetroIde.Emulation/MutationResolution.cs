namespace RetroIde.Emulation;

/// <summary>Reports an authoritative determination for an operation whose outcome was uncertain.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record MutationResolution(SessionOperation Scope, MutationDisposition Disposition, ulong StateRevision);
