namespace RetroIde.Emulation;

/// <summary>Requests a bounded, side-effect-free read. A missing map revision selects explicit legacy raw inspection.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemoryReadRequest(SessionOperation Scope, MemoryAddress Address, int ByteCount);
