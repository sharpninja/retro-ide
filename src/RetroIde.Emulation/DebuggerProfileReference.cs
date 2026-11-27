namespace RetroIde.Emulation;

/// <summary>Identifies the debugger presentation profile in the pinned bundle.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record DebuggerProfileReference(string Id, int SchemaVersion);
