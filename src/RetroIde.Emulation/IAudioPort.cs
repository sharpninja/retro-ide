namespace RetroIde.Emulation;

/// <summary>Exposes independent authenticated audio subscriptions.</summary>
public interface IAudioPort : IEmulatorPort
{
    /// <summary>Gets the versioned audio capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.audio", new Version(1, 0));

    /// <summary>Identifies the operation for opening an audio stream.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>Opens a subscription for the selected source and supplies its timing and format with the data.</summary>
    ValueTask<AdapterResult<IEmulatorMediaStream<AudioFormat>>> SubscribeAsync(MediaStreamRequest request, CancellationToken cancellationToken);
}
