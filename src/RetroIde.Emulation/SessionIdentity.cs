namespace RetroIde.Emulation;

/// <summary>Uniquely identifies the adapter, installed bundle release and live emulator instance.</summary>
public sealed record SessionIdentity(string AdapterId, string BundleId, string BundleVersion, Guid InstanceId);
