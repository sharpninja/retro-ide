namespace RetroIde.Emulation;

/// <summary>Pairs typed mutation arguments with their session, actor and precondition.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public sealed record MutationRequest<T>(SessionOperation Scope, T Payload) where T : class;
