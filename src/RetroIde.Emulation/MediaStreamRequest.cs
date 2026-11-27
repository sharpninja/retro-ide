namespace RetroIde.Emulation;

/// <summary>Identifies the actor and the display or audio source for a scoped media operation.</summary>
public sealed record MediaStreamRequest(SessionOperation Scope, string OutputId);
