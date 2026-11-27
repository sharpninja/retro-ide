using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Lists the capabilities advertised together at the given revision.</summary>
public sealed record CapabilitySnapshot(ulong Revision, ImmutableArray<CapabilityDescriptor> Items);
