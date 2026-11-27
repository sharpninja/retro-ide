namespace RetroIde.Emulation;

/// <summary>Reports an authoritative determination for an operation whose outcome was uncertain.</summary>
public sealed record MutationResolution(SessionOperation Scope, MutationDisposition Disposition, ulong StateRevision);
