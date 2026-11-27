using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Requests an authorized write to the exact current CPU mapping.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record MemoryWriteRequest(MemoryAddress Address, ImmutableArray<byte> Data);
