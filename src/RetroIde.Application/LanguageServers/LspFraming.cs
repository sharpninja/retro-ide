using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed class LspFrameReader(Stream input)
{
    private const int MaximumHeaderBytes = 8192;
    internal const int MaximumBodyBytes = 16 * 1024 * 1024;
    private readonly byte[] _buffer = new byte[8192];
    private readonly byte[] _header = new byte[MaximumHeaderBytes];
    private int _position;
    private int _end;

    internal async Task<JsonDocument?> ReadAsync(CancellationToken token)
    {
        var headerSize = await ReadHeaderAsync(token).ConfigureAwait(false);
        if (headerSize == 0)
            return null;
        var body = new byte[ReadLength(_header.AsSpan(0, headerSize))];
        var buffered = Math.Min(body.Length, _end - _position);
        _buffer.AsSpan(_position, buffered).CopyTo(body);
        _position += buffered;
        await input.ReadExactlyAsync(body.AsMemory(buffered), token).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private async ValueTask<int> ReadHeaderAsync(CancellationToken token)
    {
        for (var count = 0; count < _header.Length; count++)
        {
            if (_position == _end)
            {
                _end = await input.ReadAsync(_buffer, token).ConfigureAwait(false);
                _position = 0;
                if (_end == 0)
                    return count == 0 ? 0 : throw new EndOfStreamException("The language server stopped inside a frame header.");
            }
            var value = _buffer[_position++];
            if (value > 127)
                throw new InvalidDataException("Language server frame headers must be ASCII.");
            _header[count] = value;
            if (count >= 3 && _header.AsSpan(count - 3, 4).SequenceEqual("\r\n\r\n"u8))
                return count + 1;
        }
        throw new InvalidDataException("The language server frame header exceeded the byte limit.");
    }

    private static int ReadLength(ReadOnlySpan<byte> header)
    {
        int? length = null;
        foreach (var field in Encoding.ASCII.GetString(header).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var divider = field.IndexOf(':', StringComparison.Ordinal);
            if (divider < 1)
                throw new InvalidDataException("A language server frame header is malformed.");
            if (!field.AsSpan(0, divider).Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (length is not null || !int.TryParse(field.AsSpan(divider + 1).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed) || parsed is <= 0 or > MaximumBodyBytes)
                throw new InvalidDataException("The language server frame requires one bounded Content-Length.");
            length = parsed;
        }
        return length ?? throw new InvalidDataException("The language server frame has no Content-Length.");
    }
}
