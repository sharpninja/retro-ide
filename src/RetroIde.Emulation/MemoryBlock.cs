using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Returns bytes at the exact correlated address and observed state revision.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record MemoryBlock(SessionOperation Scope, MemoryAddress Address, ulong StateRevision, ImmutableArray<byte> Data);
