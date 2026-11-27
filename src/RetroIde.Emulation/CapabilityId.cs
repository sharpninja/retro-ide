namespace RetroIde.Emulation;

/// <summary>Identifies a namespaced feature and exact protocol version.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record CapabilityId(string Name, Version Version);
