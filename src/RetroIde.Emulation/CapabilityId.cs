namespace RetroIde.Emulation;

/// <summary>Identifies a namespaced feature and exact protocol version.</summary>
public sealed record CapabilityId(string Name, Version Version);
