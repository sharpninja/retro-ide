using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal static class BufferedEmulatorMedia
{
    internal sealed class Subscription<T> : IAsyncDisposable where T : class
    {
        private readonly Channel<MediaPacket<T>> _buffer;
        private readonly CancellationTokenSource _lifetime;
        private readonly Task<AdapterError?> _pump;
        private readonly IEmulatorMediaStream<T> _source;
        private readonly ulong _firstSequence;
        private readonly ILogger _logger;

        internal Subscription(IEmulatorMediaStream<T> source, int capacity, int maxBytes,
            Func<T, int, bool> validFormat, ILogger logger, CancellationToken token)
        {
            _source = source;
            _firstSequence = source.FirstSequence;
            _logger = logger;
            _buffer = Channel.CreateBounded<MediaPacket<T>>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _pump = Task.Run(() => PumpAsync(source, _buffer.Writer, maxBytes, validFormat, logger, _lifetime.Token), CancellationToken.None);
        }

        internal async IAsyncEnumerable<AdapterResult<MediaPacket<T>>> ReadAsync([EnumeratorCancellation] CancellationToken token)
        {
            MediaPacket<T>? previous = null;
            await foreach (var item in _buffer.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                var skipped = previous is null ? item.Sequence != _firstSequence : item.Sequence - previous.Sequence != 1;
                var changed = previous is not null &&
                    (!string.Equals(item.Timestamp.ClockDomain, previous.Timestamp.ClockDomain, StringComparison.Ordinal) ||
                     item.Timestamp.TicksPerSecond != previous.Timestamp.TicksPerSecond ||
                     item.Timestamp.Ticks < previous.Timestamp.Ticks ||
                     !EqualityComparer<T>.Default.Equals(item.Format, previous.Format));
                previous = item;
                yield return AdapterResult<MediaPacket<T>>.Success(
                    skipped || changed ? item with { Discontinuous = true } : item);
            }
            if (await _pump.ConfigureAwait(false) is { } error)
                yield return AdapterResult<MediaPacket<T>>.Failure(error);
        }

        public async ValueTask DisposeAsync()
        {
            using (_lifetime)
            {
                try { await _lifetime.CancelAsync().ConfigureAwait(false); }
                catch (Exception error) when (RuntimeFailures.CanRecover(error))
                {
                    RuntimeFailures.Report(_logger, error, "cancel emulator media pump");
                }

                try { await _pump.ConfigureAwait(false); }
                finally { await _source.DisposeAsync().ConfigureAwait(false); }
            }
        }
    }

    private static async Task<AdapterError?> PumpAsync<T>(IEmulatorMediaStream<T> source, ChannelWriter<MediaPacket<T>> destination,
        int maxBytes, Func<T, int, bool> validFormat, ILogger logger, CancellationToken token) where T : class
    {
        ulong? previous = null;
        try
        {
            await foreach (var packet in source.ReadPacketsAsync(token).WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (!Valid(packet, source.Session, source.StreamId, maxBytes, validFormat) ||
                    packet.Sequence < source.FirstSequence || packet.Sequence <= previous)
                    return new(AdapterErrorCodes.Incompatible, "The emulator supplied invalid media or a non-increasing sequence.");

                previous = packet.Sequence;
                destination.TryWrite(packet);
            }
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "read emulator media");
            return new(AdapterErrorCodes.Disconnected, "The emulator media subscription disconnected.");
        }
        finally { destination.TryComplete(); }
    }

    internal static bool Valid<T>(MediaPacket<T> packet, SessionIdentity session, Guid stream, int maxBytes, Func<T, int, bool> validFormat)
        where T : class =>
        packet.Session == session && stream != Guid.Empty && packet.StreamId == stream &&
        packet.Timestamp is { TicksPerSecond: > 0 } time && !string.IsNullOrWhiteSpace(time.ClockDomain) &&
        !packet.Data.IsDefaultOrEmpty && packet.Data.Length <= maxBytes && packet.Format is { } format && validFormat(format, packet.Data.Length);
}
