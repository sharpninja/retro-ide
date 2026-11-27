using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Describes adapter identity, supported .NET contract versions, modes and known machine families.</summary>
public sealed record AdapterDescriptor(
    string AdapterId,
    ImmutableArray<Version> ContractVersions,
    ImmutableArray<SessionMode> Modes,
    ImmutableArray<string> TargetFamilies);
