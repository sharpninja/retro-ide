namespace RetroIde.Emulation;

/// <summary>Exposes independent frame capture and continuous video capabilities.</summary>
public interface IVideoPort : IEmulatorPort
{
    /// <summary>Gets the versioned video capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.video", new Version(1, 0));

    /// <summary>Identifies the operation for requesting a single frame.</summary>
    public const string Capture = "capture";

    /// <summary>Identifies the operation for opening a continuous frame stream.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>Captures one frame when the adapter advertises capture for this display.</summary>
    ValueTask<AdapterResult<MediaPacket<VideoFormat>>> CaptureAsync(MediaStreamRequest request, CancellationToken cancellationToken);

    /// <summary>Opens a continuous video subscription for the selected display.</summary>
    ValueTask<AdapterResult<IEmulatorMediaStream<VideoFormat>>> SubscribeAsync(MediaStreamRequest request, CancellationToken cancellationToken);
}
