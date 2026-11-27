using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Describes a target clock and its reference phases. A missing frequency denotes an unknown or variable rate.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public sealed record ClockDomainDescriptor(string Id, ulong? NominalHertz, ImmutableArray<string> Phases);
