using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RetroIde.LspFixture;

/// <summary>Reads and writes the native stdio wire format for the deterministic test server.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-014#ac-test-language-session, ac-test-language-documents. Use byte framing so multibyte document content exercises the client's transport.</remarks>
internal sealed class LspConnection(Stream input, Stream output)
{
    private const int MaxHeaderBytes = 8192;
    private const int MaxContentBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads a complete framed JSON document, or returns null when the client closes between frames.</summary>
    /// <returns>The parsed request or notification, owned by the caller.</returns>
    /// <exception cref="InvalidDataException">The frame headers or byte length are invalid.</exception>
    /// <exception cref="EndOfStreamException">The client closes during a frame.</exception>
    public JsonDocument? Read()
    {
        var header = new StringBuilder();
        while (header.Length < MaxHeaderBytes)
        {
            var next = input.ReadByte();
            if (next < 0 && header.Length == 0)
                return null;
            if (next < 0)
                throw new EndOfStreamException("The client closed during the LSP header.");
            if (next > 127)
                throw new InvalidDataException("LSP headers must use ASCII.");
            header.Append((char)next);
            var end = header.Length;
            if (end < 4 || header[end - 4] != '\r' || header[end - 3] != '\n' || header[end - 2] != '\r' || header[end - 1] != '\n')
                continue;

            const string Length = "Content-Length:";
            var definitions = header.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var lengths = definitions.Where(line => line.StartsWith(Length, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (lengths.Length != 1 || !int.TryParse(lengths[0].AsSpan(Length.Length).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var size) || size is <= 0 or > MaxContentBytes)
                throw new InvalidDataException("The LSP frame requires one bounded Content-Length.");

            var body = new byte[size];
            input.ReadExactly(body);
            return JsonDocument.Parse(body);
        }
        throw new InvalidDataException("The LSP header exceeded the fixture byte limit.");
    }

    /// <summary>Writes one message and flushes it to the client's standard output pipe.</summary>
    /// <param name="message">A JSON-RPC response or notification.</param>
    public void Send<T>(T message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}
