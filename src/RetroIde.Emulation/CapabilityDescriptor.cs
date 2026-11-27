using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Advertises currently supported operations and quantitative limits for an optional subject.</summary>
/// <param name="Id">Versioned feature identifier. Clients can ignore unknown feature identifiers.</param>
/// <param name="SubjectId">Stable target identifier within the session, when the capability applies to a specific device or CPU.</param>
/// <param name="Operations">The operation identifiers supported for this subject and feature.</param>
/// <param name="Limits">Feature-defined bounds with units specified by the feature contract.</param>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record CapabilityDescriptor(
    CapabilityId Id,
    string? SubjectId,
    ImmutableArray<string> Operations,
    ImmutableDictionary<string, ulong> Limits);
