using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RetroIde.Application.Emulators;
using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    private static readonly VideoFormat FrameFormat = new(2, 1, 8, VideoPixelFormat.Bgra32);
    private static readonly AudioFormat SoundFormat = new(48_000, 2, "pcm-s16le", 2);

    /// <summary>Floods a video stream while control and state queries continue.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
    [Fact]
    public async Task Video_retains_the_newest_frames_without_stalling_control()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var source = new BurstMedia<VideoFormat>(adapter.Identity, FrameFormat);
        adapter.VideoSource = source;
        await VerifyFloodAsync(adapter, control, source, EmulatorSessionControl.VideoQueueCapacity,
            control.WatchVideoAsync(new(adapter.Scope("desktop"), "display"), token), token).ConfigureAwait(true);
    }

    /// <summary>Floods an audio stream, retaining bounded, ordered packets with an explicit discontinuity.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
    [Fact]
    public async Task Audio_marks_dropped_packets_and_does_not_stall_control()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var source = new BurstMedia<AudioFormat>(adapter.Identity, SoundFormat);
        adapter.AudioSource = source;
        await VerifyFloodAsync(adapter, control, source, EmulatorSessionControl.AudioQueueCapacity,
            control.WatchAudioAsync(new(adapter.Scope("desktop"), "speaker"), token), token).ConfigureAwait(true);
    }

    /// <summary>Checks subscription cleanup when the consumer leaves with the source still producing.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
    [Fact]
    public async Task A_consumer_leaving_an_active_stream_stops_the_producer_and_releases_the_subscription()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var source = new BurstMedia<VideoFormat>(adapter.Identity, FrameFormat);
        adapter.VideoSource = source;
        var reader = adapter.StartControl().WatchVideoAsync(new(adapter.Scope("desktop"), "display"), token).GetAsyncEnumerator(token);
        await using (reader.ConfigureAwait(true))
            Assert.True(await reader.MoveNextAsync());

        Assert.True(source.Ended);
        Assert.True(source.Disposed);
        Assert.False(source.Flood.Task.IsCompleted);
    }

    /// <summary>Checks capture-only advertisement, exact instance scope, and frame payload validation.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
    [Fact]
    public async Task Capture_eligibility_is_independent_of_streaming_and_rejects_invalid_frames()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var snapshot = adapter.Current.Capabilities;
        adapter.Current = adapter.Current with
        {
            Capabilities = snapshot with
            {
                Items = snapshot.Items.Select(item => item.Id == IVideoPort.Capability
                    ? item with { Operations = [IVideoPort.Capture] } : item).ToImmutableArray(),
            },
        };
        var control = adapter.StartControl();
        var request = new MediaStreamRequest(adapter.Scope("desktop"), "display");
        var frame = await control.CaptureVideoAsync(request, token);
        Assert.True(frame.Succeeded);
        Assert.Equal(FrameFormat, frame.Value.Format);
        Assert.Equal(AdapterErrorCodes.Unsupported,
            Assert.Single(await control.WatchVideoAsync(request, token).ToArrayAsync(token)).Error?.Code, StringComparer.Ordinal);

        var foreign = request with { Scope = request.Scope with { Session = adapter.Identity with { InstanceId = Guid.NewGuid() } } };
        Assert.Equal(AdapterErrorCodes.Incompatible, (await control.CaptureVideoAsync(foreign, token)).Error?.Code, StringComparer.Ordinal);

        adapter.CaptureFrame = frame.Value with { Data = [0] };
        Assert.Equal(AdapterErrorCodes.Incompatible, (await control.CaptureVideoAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        adapter.CaptureFrame = frame.Value with { Format = new(17, 1, 68, VideoPixelFormat.Bgra32), Data = [.. new byte[68]] };
        Assert.Equal(AdapterErrorCodes.Incompatible, (await control.CaptureVideoAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        Assert.Equal(0, adapter.SubscriptionsOpened);
    }

    /// <summary>Checks that a binding cannot attach a source from another emulator instance.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
    [Fact]
    public async Task A_foreign_media_source_is_released_without_reading_packets()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var foreign = adapter.Identity with { InstanceId = Guid.NewGuid() };
        var source = new BurstMedia<AudioFormat>(foreign, SoundFormat);
        adapter.AudioSource = source;
        var request = new MediaStreamRequest(adapter.Scope("desktop"), "speaker");

        var received = await adapter.StartControl().WatchAudioAsync(request, token).ToArrayAsync(token);

        Assert.Equal(AdapterErrorCodes.Incompatible, Assert.Single(received).Error?.Code, StringComparer.Ordinal);
        Assert.True(source.Disposed);
        Assert.False(source.Ended);
    }

    private static async Task VerifyFloodAsync<T>(ControlAdapter adapter, EmulatorSessionControl control, BurstMedia<T> source,
        int capacity, IAsyncEnumerable<AdapterResult<MediaPacket<T>>> media, CancellationToken token) where T : class
    {
        var reader = media.GetAsyncEnumerator(token);
        var tail = new List<MediaPacket<T>>();
        await using (reader.ConfigureAwait(false))
        {
            Assert.True(await reader.MoveNextAsync().ConfigureAwait(false));
            Assert.Equal(source.FirstSequence, reader.Current.Value?.Sequence);
            Assert.False(reader.Current.Value?.Discontinuous);
            source.Flood.TrySetResult();
            await source.Filled.Task.WaitAsync(token).ConfigureAwait(false);

            Assert.True((await control.ReadAsync(token).ConfigureAwait(false)).Succeeded);
            Assert.True((await control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), token).ConfigureAwait(false)).Succeeded);
            Assert.False(source.Ended);
            source.Finish.TrySetResult();

            while (await reader.MoveNextAsync().ConfigureAwait(false))
            {
                Assert.True(reader.Current.Succeeded, reader.Current.Error?.Message);
                tail.Add(reader.Current.Value!);
            }
        }
        Assert.True(source.Ended);
        Assert.True(source.Disposed);
        Assert.Equal(capacity, tail.Count);
        Assert.Equal(source.LastSequence - (ulong)capacity + 1, tail[0].Sequence);
        Assert.Equal(source.LastSequence, tail[^1].Sequence);
        Assert.True(tail[0].Discontinuous);
        Assert.All(tail.Skip(1), packet => Assert.False(packet.Discontinuous));
        Assert.All(tail, packet => Assert.Equal(new MediaTimestamp("fixture.clock", packet.Sequence * 10, 1_000), packet.Timestamp));
    }

    private sealed class BurstMedia<T>(SessionIdentity session, T format) : IEmulatorMediaStream<T> where T : class
    {
        public SessionIdentity Session { get; } = session;
        public Guid StreamId { get; } = Guid.NewGuid();
        public ulong FirstSequence => 10;
        internal ulong LastSequence => 138;
        internal TaskCompletionSource Flood { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Filled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Ended { get; private set; }
        internal bool Disposed { get; private set; }

        public async IAsyncEnumerable<MediaPacket<T>> ReadPacketsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                yield return Packet(FirstSequence);
                await Flood.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                for (var sequence = FirstSequence + 1; sequence <= LastSequence; sequence++)
                    yield return Packet(sequence);

                Filled.TrySetResult();
                await Finish.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { Ended = true; }
        }

        private MediaPacket<T> Packet(ulong sequence) =>
            new(Session, StreamId, sequence, new("fixture.clock", sequence * 10, 1_000), format, [0, 0, 0, 0, 0, 0, 0, 0]);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed partial class ControlAdapter : IVideoPort, IAudioPort
    {
        private static readonly ImmutableDictionary<string, ulong> MediaBounds =
            ImmutableDictionary<string, ulong>.Empty.Add("max-packet-bytes", 64);
        private static readonly CapabilityDescriptor Video = new(IVideoPort.Capability, "display", [IVideoPort.Capture, IVideoPort.Subscribe], MediaBounds);
        private static readonly CapabilityDescriptor Audio = new(IAudioPort.Capability, "speaker", [IAudioPort.Subscribe], MediaBounds);

        internal BurstMedia<VideoFormat>? VideoSource { get; set; }
        internal BurstMedia<AudioFormat>? AudioSource { get; set; }
        internal MediaPacket<VideoFormat>? CaptureFrame { get; set; }
        internal int SubscriptionsOpened { get; private set; }

        public ValueTask<AdapterResult<MediaPacket<VideoFormat>>> CaptureAsync(MediaStreamRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<MediaPacket<VideoFormat>>.Success(CaptureFrame ??
                new(Identity, Guid.NewGuid(), 1, new("fixture.clock", 10, 1_000), FrameFormat, [0, 0, 0, 0, 0, 0, 0, 0])));

        ValueTask<AdapterResult<IEmulatorMediaStream<VideoFormat>>> IVideoPort.SubscribeAsync(MediaStreamRequest request,
            CancellationToken cancellationToken) => OpenMediaAsync(VideoSource);

        ValueTask<AdapterResult<IEmulatorMediaStream<AudioFormat>>> IAudioPort.SubscribeAsync(MediaStreamRequest request,
            CancellationToken cancellationToken) => OpenMediaAsync(AudioSource);

        private ValueTask<AdapterResult<IEmulatorMediaStream<T>>> OpenMediaAsync<T>(IEmulatorMediaStream<T>? source) where T : class
        {
            SubscriptionsOpened++;
            return ValueTask.FromResult(source is null
                ? AdapterResult<IEmulatorMediaStream<T>>.Failure(new(AdapterErrorCodes.Unavailable, "No media stream."))
                : AdapterResult<IEmulatorMediaStream<T>>.Success(source));
        }
    }
}
