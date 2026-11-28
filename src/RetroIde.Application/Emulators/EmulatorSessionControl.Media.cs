using System.Runtime.CompilerServices;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal const int VideoQueueCapacity = 3;
    internal const int AudioQueueCapacity = 16;
    private const int VideoPacketLimit = 32 * 1024 * 1024;
    private const int AudioPacketLimit = 1024 * 1024;

    internal ValueTask<AdapterResult<MediaPacket<VideoFormat>>> CaptureVideoAsync(MediaStreamRequest request, CancellationToken token) =>
        InvokeMediaAsync<IVideoPort, MediaPacket<VideoFormat>>(request, IVideoPort.Capability, IVideoPort.Capture, async (port, descriptor) =>
        {
            var result = await port.CaptureAsync(request, token).ConfigureAwait(false);
            if (!result.Succeeded)
                return result;

            var packet = result.Value;
            return BufferedEmulatorMedia.Valid(packet, _identity, packet.StreamId, PacketLimit(descriptor, VideoPacketLimit), ValidVideo)
                ? result : Fail<MediaPacket<VideoFormat>>(AdapterErrorCodes.Incompatible, "The emulator returned an invalid captured frame.", request.Scope.OperationId);
        }, token);

    internal IAsyncEnumerable<AdapterResult<MediaPacket<VideoFormat>>> WatchVideoAsync(MediaStreamRequest request, CancellationToken token) =>
        ReadMediaAsync<IVideoPort, VideoFormat>(request, IVideoPort.Capability, IVideoPort.Subscribe, VideoQueueCapacity, VideoPacketLimit,
            static (port, source, cancellation) => port.SubscribeAsync(source, cancellation), ValidVideo, token);

    internal IAsyncEnumerable<AdapterResult<MediaPacket<AudioFormat>>> WatchAudioAsync(MediaStreamRequest request, CancellationToken token) =>
        ReadMediaAsync<IAudioPort, AudioFormat>(request, IAudioPort.Capability, IAudioPort.Subscribe, AudioQueueCapacity, AudioPacketLimit,
            static (port, source, cancellation) => port.SubscribeAsync(source, cancellation), ValidAudio, token);

    private async IAsyncEnumerable<AdapterResult<MediaPacket<T>>> ReadMediaAsync<TPort, T>(
        MediaStreamRequest request, CapabilityId capability, string operation, int capacity, int hardLimit,
        Func<TPort, MediaStreamRequest, CancellationToken, ValueTask<AdapterResult<IEmulatorMediaStream<T>>>> open,
        Func<T, int, bool> validFormat, [EnumeratorCancellation] CancellationToken token)
        where TPort : class, IEmulatorPort where T : class
    {
        using var subscriptionLifetime = _sessionLifetime.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(token, _sessionLifetime) : null;
        token = subscriptionLifetime?.Token ?? token;
        var opened = await InvokeMediaAsync<TPort, MediaSubscription<T>>(request, capability, operation, async (port, descriptor) =>
        {
            var maxBytes = PacketLimit(descriptor, hardLimit);
            var result = await open(port, request, token).ConfigureAwait(false);
            return result.Succeeded
                ? AdapterResult<MediaSubscription<T>>.Success(new(result.Value, maxBytes))
                : AdapterResult<MediaSubscription<T>>.Failure(result.Error);
        }, token).ConfigureAwait(false);

        if (!opened.Succeeded)
        {
            yield return AdapterResult<MediaPacket<T>>.Failure(opened.Error);
            yield break;
        }

        var subscription = opened.Value;
        try
        {
            if (subscription.Source.Session != _identity || subscription.Source.StreamId == Guid.Empty)
            {
                yield return Fail<MediaPacket<T>>(AdapterErrorCodes.Incompatible,
                    "The media subscription belongs to a different emulator session.", request.Scope.OperationId);
                yield break;
            }

            await foreach (var item in BufferedEmulatorMedia.ReadAsync(subscription.Source, capacity, subscription.MaxBytes,
                validFormat, _logger, token).ConfigureAwait(false))
                yield return item.Succeeded ? item : AdapterResult<MediaPacket<T>>.Failure(item.Error with { OperationId = request.Scope.OperationId });
        }
        finally { await ReleaseMediaAsync(subscription.Source).ConfigureAwait(false); }
    }

    private async ValueTask<AdapterResult<T>> InvokeMediaAsync<TPort, T>(MediaStreamRequest request, CapabilityId capability,
        string operation, Func<TPort, CapabilityDescriptor, ValueTask<AdapterResult<T>>> invoke, CancellationToken token)
        where TPort : class, IEmulatorPort where T : class
    {
        if (string.IsNullOrWhiteSpace(request.OutputId))
            return Fail<T>(AdapterErrorCodes.InvalidArgument, "Select a media output.", request.Scope.OperationId);
        return await InvokePortAsync(request.Scope, capability, request.OutputId, operation, invoke, token).ConfigureAwait(false);
    }

    private async ValueTask ReleaseMediaAsync<T>(IEmulatorMediaStream<T> source) where T : class
    {
        try { await source.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "release emulator media subscription");
        }
    }

    private static int PacketLimit(CapabilityDescriptor descriptor, int hardLimit) =>
        descriptor.Limits.TryGetValue("max-packet-bytes", out var advertised) ? (int)Math.Min((ulong)hardLimit, advertised) : hardLimit;

    private static bool ValidVideo(VideoFormat format, int length)
    {
        var bytesPerPixel = format.PixelFormat switch
        {
            VideoPixelFormat.Bgra32 or VideoPixelFormat.Rgba32 => 4,
            VideoPixelFormat.Rgb24 => 3,
            _ => 0,
        };
        return bytesPerPixel != 0 && format.Width > 0 && format.Height > 0 &&
            format.Stride >= (long)format.Width * bytesPerPixel && length == (long)format.Stride * format.Height;
    }

    private static bool ValidAudio(AudioFormat format, int length) =>
        format.SampleRate > 0 && format.Channels > 0 && !string.IsNullOrWhiteSpace(format.Encoding) &&
        (format.BytesPerSample is null ||
            (format.BytesPerSample is > 0 && length % ((long)format.Channels * format.BytesPerSample.Value) == 0));

    private sealed record MediaSubscription<T>(IEmulatorMediaStream<T> Source, int MaxBytes) where T : class;
}
