namespace RetroIde.Application.Terminals;

internal sealed class TerminalTranscript(int capacity)
{
    internal sealed record Slice(long StartOffset, long NextOffset, long OldestOffset, bool Truncated, byte[] Bytes);

    private readonly Lock _gate = new();
    private readonly byte[] _storage = new byte[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private long _end;

    internal void Append(ReadOnlySpan<byte> output)
    {
        lock (_gate)
        {
            if (output.Length > _storage.Length)
            {
                _end += output.Length - _storage.Length;
                output = output[^_storage.Length..];
            }
            var start = (int)(_end % _storage.Length);
            var first = Math.Min(output.Length, _storage.Length - start);
            output[..first].CopyTo(_storage.AsSpan(start));
            output[first..].CopyTo(_storage);
            _end += output.Length;
        }
    }

    internal Slice Read(long offset, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        lock (_gate)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _end);
            var oldest = Math.Max(0, _end - _storage.Length);
            var start = Math.Max(offset, oldest);
            var count = (int)Math.Min(maximumBytes, _end - start);
            var result = new byte[count];
            var position = (int)(start % _storage.Length);
            var first = Math.Min(count, _storage.Length - position);
            _storage.AsSpan(position, first).CopyTo(result);
            _storage.AsSpan(0, count - first).CopyTo(result.AsSpan(first));
            return new Slice(start, start + count, oldest, start != offset, result);
        }
    }
}
