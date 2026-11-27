using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Lists the capabilities advertised together at the given revision.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record CapabilitySnapshot(ulong Revision, ImmutableArray<CapabilityDescriptor> Items);
