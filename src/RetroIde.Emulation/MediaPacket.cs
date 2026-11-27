using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Carries immutable frame or audio data and the format in effect at that sequence.</summary>
public sealed record MediaPacket<TFormat>(
    SessionIdentity Session,
    Guid StreamId,
    ulong Sequence,
    MediaTimestamp Timestamp,
    TFormat Format,
    ImmutableArray<byte> Data,
    bool Discontinuous = false) where TFormat : class;
