namespace RetroIde.Emulation;

/// <summary>Uniquely identifies the adapter, installed bundle release and live emulator instance.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public sealed record SessionIdentity(string AdapterId, string BundleId, string BundleVersion, Guid InstanceId);
