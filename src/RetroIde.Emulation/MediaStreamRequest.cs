namespace RetroIde.Emulation;

/// <summary>Identifies the actor and the display or audio source for a scoped media operation.</summary>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public sealed record MediaStreamRequest(SessionOperation Scope, string OutputId);
