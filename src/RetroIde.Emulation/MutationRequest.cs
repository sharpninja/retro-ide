namespace RetroIde.Emulation;

/// <summary>Pairs typed mutation arguments with their session, actor and precondition.</summary>
public sealed record MutationRequest<T>(SessionOperation Scope, T Payload) where T : class;
