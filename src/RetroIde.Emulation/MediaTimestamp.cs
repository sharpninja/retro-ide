namespace RetroIde.Emulation;

/// <summary>Locates a sample in the producer's declared clock domain.</summary>
public sealed record MediaTimestamp(string ClockDomain, ulong Ticks, ulong TicksPerSecond);
