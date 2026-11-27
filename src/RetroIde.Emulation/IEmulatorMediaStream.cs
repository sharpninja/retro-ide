namespace RetroIde.Emulation;

/// <summary>A per-consumer subscription independent of emulator control traffic.</summary>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public interface IEmulatorMediaStream<TFormat> : IAsyncDisposable where TFormat : class
{
    /// <summary>Gets the session providing the media data.</summary>
    SessionIdentity Session { get; }

    /// <summary>Gets the unique identity of this subscription.</summary>
    Guid StreamId { get; }

    /// <summary>Gets the sequence expected for the first delivered packet.</summary>
    ulong FirstSequence { get; }

    /// <summary>Reads the ordered stream until closed or cancelled.</summary>
    IAsyncEnumerable<MediaPacket<TFormat>> ReadPacketsAsync(CancellationToken cancellationToken);
}
