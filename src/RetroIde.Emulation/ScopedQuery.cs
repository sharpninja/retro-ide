namespace RetroIde.Emulation;

/// <summary>Associates typed query arguments with their exact session, caller and precondition.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record ScopedQuery<T>(SessionOperation Scope, T Payload) where T : class;
