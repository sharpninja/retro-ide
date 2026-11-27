namespace RetroIde.Emulation;

/// <summary>Locates a sample in the producer's declared clock domain.</summary>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public sealed record MediaTimestamp(string ClockDomain, ulong Ticks, ulong TicksPerSecond);
