namespace RetroIde.Emulation;

/// <summary>Requests a bounded, side-effect-free read. A missing map revision selects explicit legacy raw inspection.</summary>
public sealed record MemoryReadRequest(SessionOperation Scope, MemoryAddress Address, int ByteCount);
