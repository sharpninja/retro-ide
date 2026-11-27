using System.Text;
using System.Text.Json;

namespace RetroIde.LspFixture;

/// <summary>Runs a deterministic language server on stdio for independent client verification.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-014#ac-test-language-session, ac-test-language-documents, ac-test-language-features. Provides negotiated encoding, in-memory text, deferred responses and controlled exits.</remarks>
internal sealed class Program(LspConnection connection, string preferredEncoding)
{
    private readonly Dictionary<string, OpenDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingCompletion> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _workspaceFolders = new(StringComparer.Ordinal);
    private string _encoding = "utf-16";
    private bool _initialized;
    private bool _shutDown;
    private bool _deferCompletions;
    private bool _crashOnFirstCompletion;
    private string? _traceDirectory;

    /// <summary>Starts a reference server that prefers the specified position encoding.</summary>
    /// <param name="args">An optional preferred encoding of utf-8, utf-16, or utf-32.</param>
    /// <returns>A protocol exit status, or two when the arguments or input framing are invalid.</returns>
    private static int Main(string[] args)
    {
        var preferred = args.Length == 0 ? "utf-8" : args[0];
        if (args.Length > 1 || preferred is not ("utf-8" or "utf-16" or "utf-32"))
        {
            Console.Error.WriteLine("Usage: RetroIde.LspFixture [utf-8|utf-16|utf-32]");
            return 2;
        }

        using var input = new BufferedStream(Console.OpenStandardInput());
        using var output = Console.OpenStandardOutput();
        var server = new Program(new LspConnection(input, output), preferred);
        try { return server.Run(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or EndOfStreamException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    /// <summary>Processes messages in wire order until the client exits or closes the input pipe.</summary>
    /// <returns>The selected fixture exit code.</returns>
    private int Run()
    {
        while (connection.Read() is { } message)
        {
            using (message)
            {
                var packet = message.RootElement;
                if (!packet.TryGetProperty("method", out var method))
                    continue;
                packet.TryGetProperty("id", out var id);
                packet.TryGetProperty("params", out var parameters);
                if (Dispatch(method.GetString(), id, parameters) is { } exitCode)
                    return exitCode;
            }
        }
        return _shutDown ? 0 : 1;
    }

    /// <summary>Applies one supported language-server message to the fixture session.</summary>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="id">A request ID, or an undefined element for notifications.</param>
    /// <param name="parameters">The request's optional payload.</param>
    /// <returns>The exit code when the message terminates the fixture, or null to continue.</returns>
    private int? Dispatch(string? method, JsonElement id, JsonElement parameters)
    {
        if (method is "exit")
            return _shutDown ? 0 : 1;
        var request = id.ValueKind is JsonValueKind.String or JsonValueKind.Number;
        if (_shutDown || (!_initialized && method is not "initialize"))
        {
            if (request)
                SendError(id, _shutDown ? -32600 : -32002, _shutDown ? "Server shut down." : "Server not initialized.");
            return null;
        }

        switch (method)
        {
            case "initialize" when request && !_initialized:
                Initialize(id, parameters);
                break;
            case "initialized":
            case "textDocument/didSave":
                break;
            case "workspace/didChangeWorkspaceFolders":
                ChangeFolders(parameters.GetProperty("event"));
                break;
            case "textDocument/didOpen":
                var opened = parameters.GetProperty("textDocument");
                var uri = opened.GetProperty("uri").GetString()!;
                var document = new OpenDocument(opened.GetProperty("version").GetInt32(), opened.GetProperty("text").GetString()!);
                _documents.Add(uri, document);
                PublishDiagnostics(uri, document.Version, document.Text);
                break;
            case "textDocument/didChange":
                ApplyChanges(parameters);
                break;
            case "textDocument/didClose":
                var closed = DocumentUri(parameters);
                _documents.Remove(closed);
                PublishDiagnostics(closed, null, string.Empty);
                break;
            case "textDocument/completion" when request:
                if (_crashOnFirstCompletion && _traceDirectory is { } directory &&
                    !File.Exists(Path.Combine(directory, "crashed")))
                {
                    Trace("crashed", id.GetRawText());
                    return 17;
                }
                CompleteDocument(id, parameters);
                break;
            case "$/cancelRequest":
                var key = parameters.GetProperty("id").GetRawText();
                if (_pending.TryGetValue(key, out var pending))
                {
                    _pending[key] = pending with { Cancelled = true };
                    Trace("cancel-received", key);
                }
                break;
            case "fixture/releaseCompletion":
                ReleaseCompletion(parameters);
                break;
            case "fixture/crash":
                return 17;
            case "shutdown" when request:
                _shutDown = true;
                connection.Send(new { jsonrpc = "2.0", id, result = (object?)null });
                break;
            default:
                if (request)
                    SendError(id, -32601, "Fixture method is unavailable.");
                break;
        }
        return null;
    }

    /// <summary>Negotiates encoding and enables deterministic completion delays when requested by the test.</summary>
    /// <param name="id">The initialize request ID.</param>
    /// <param name="parameters">Client capabilities and optional fixture controls.</param>
    private void Initialize(JsonElement id, JsonElement parameters)
    {
        if (parameters.TryGetProperty("capabilities", out var capabilities) &&
            capabilities.TryGetProperty("general", out var general) && general.TryGetProperty("positionEncodings", out var offered) &&
            offered.EnumerateArray().Any(value => string.Equals(value.GetString(), preferredEncoding, StringComparison.Ordinal)))
            _encoding = preferredEncoding;

        _deferCompletions = parameters.TryGetProperty("initializationOptions", out var options) &&
            options.ValueKind == JsonValueKind.Object && options.TryGetProperty("deferCompletions", out var defer) && defer.ValueKind == JsonValueKind.True;
        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("traceDirectory", out var trace) &&
            trace.ValueKind == JsonValueKind.String)
            _traceDirectory = trace.GetString();
        _crashOnFirstCompletion = options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("crashOnFirstCompletion", out var crash) && crash.ValueKind == JsonValueKind.True;
        var folderMode = options.ValueKind == JsonValueKind.Object && options.TryGetProperty("workspaceFolderMode", out var mode)
            ? mode.GetString() : null;
        foreach (var folder in parameters.GetProperty("workspaceFolders").EnumerateArray())
            _workspaceFolders.Add(folder.GetProperty("uri").GetString()!);
        _initialized = true;
        connection.Send(new
        {
            jsonrpc = "2.0", id,
            result = new
            {
                capabilities = new
                {
                    positionEncoding = _encoding,
                    textDocumentSync = new { openClose = true, change = 2, save = true },
                    completionProvider = new { },
                    workspace = new { workspaceFolders = new { supported = folderMode is "initial" or "dynamic", changeNotifications = folderMode is "dynamic" } },
                },
                serverInfo = new { name = "Retro IDE LSP test fixture" },
            },
        });
    }

    private void ChangeFolders(JsonElement folders)
    {
        foreach (var folder in folders.GetProperty("removed").EnumerateArray())
            _workspaceFolders.Remove(folder.GetProperty("uri").GetString()!);
        foreach (var folder in folders.GetProperty("added").EnumerateArray())
            _workspaceFolders.Add(folder.GetProperty("uri").GetString()!);
    }

    /// <summary>Applies ordered full or ranged content changes to the current in-memory document.</summary>
    /// <param name="parameters">The document version and successive change records.</param>
    private void ApplyChanges(JsonElement parameters)
    {
        var target = parameters.GetProperty("textDocument");
        var uri = target.GetProperty("uri").GetString()!;
        var version = target.GetProperty("version").GetInt32();
        var old = _documents[uri];
        if (version <= old.Version)
            throw new InvalidDataException("The document version did not advance.");
        var text = old.Text;
        foreach (var change in parameters.GetProperty("contentChanges").EnumerateArray())
        {
            var replacement = change.GetProperty("text").GetString()!;
            if (change.TryGetProperty("range", out var range))
            {
                var start = Offset(text, range.GetProperty("start"));
                var end = Offset(text, range.GetProperty("end"));
                if (end < start)
                    throw new InvalidDataException("The document edit range is reversed.");
                text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
            }
            else
            {
                text = replacement;
            }
        }
        _documents[uri] = new(version, text);
        PublishDiagnostics(uri, version, text);
    }

    /// <summary>Captures a completion reply with URI, version, text, and the converted request offset.</summary>
    /// <param name="id">The client request ID.</param>
    /// <param name="parameters">The document URI and negotiated position.</param>
    private void CompleteDocument(JsonElement id, JsonElement parameters)
    {
        var uri = DocumentUri(parameters);
        if (!_documents.TryGetValue(uri, out var snapshot))
        {
            SendError(id, -32602, "The document is not open.");
            return;
        }
        var completion = new PendingCompletion(id.Clone(), uri, snapshot, Offset(snapshot.Text, parameters.GetProperty("position")));
        if (_deferCompletions)
        {
            _pending.Add(id.GetRawText(), completion);
            Trace("completion-held", id.GetRawText());
            connection.Send(new { jsonrpc = "2.0", method = "fixture/completionHeld", @params = new { id, uri, version = snapshot.Version } });
        }
        else
        {
            SendCompletion(completion);
        }
    }

    private void Trace(string name, string value)
    {
        if (_traceDirectory is { } directory)
            File.WriteAllText(Path.Combine(directory, name), value);
    }

    /// <summary>Releases a held completion with its original contents, or returns the native cancellation error.</summary>
    /// <param name="parameters">The held request ID and an optional instruction to send a late reply.</param>
    private void ReleaseCompletion(JsonElement parameters)
    {
        if (!_pending.Remove(parameters.GetProperty("id").GetRawText(), out var pending))
            return;
        var sendLate = parameters.TryGetProperty("sendLate", out var late) && late.ValueKind == JsonValueKind.True;
        if (pending.Cancelled && !sendLate)
            SendError(pending.Id, -32800, "The completion was cancelled.");
        else
            SendCompletion(pending);
    }

    /// <summary>Sends the original document snapshot in a completion item for client correlation assertions.</summary>
    /// <param name="completion">The reply captured when the request arrived.</param>
    private void SendCompletion(PendingCompletion completion) => connection.Send(new
    {
        jsonrpc = "2.0", id = completion.Id,
        result = new[] { new { label = "fixture completion", detail = completion.Document.Text,
            data = new { uri = completion.Uri, version = completion.Document.Version, offset = completion.Offset, workspaceFolders = _workspaceFolders.Order(StringComparer.Ordinal).ToArray() } } },
    });

    /// <summary>Publishes a deterministic first-line diagnostic when the fixture text has the error marker.</summary>
    /// <param name="uri">The affected document URI.</param>
    /// <param name="version">The current document version, or null for an empty close notification.</param>
    /// <param name="text">The current buffer.</param>
    private void PublishDiagnostics(string uri, int? version, string text)
    {
        var diagnostics = text.StartsWith("ERROR", StringComparison.Ordinal)
            ? new object[] { new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
                severity = 1, source = "fixture", message = "Fixture error marker." } }
            : [];
        connection.Send(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri, version, diagnostics } });
    }

    /// <summary>Converts a line and encoded character coordinate to an offset in the .NET source string.</summary>
    /// <param name="text">The document snapshot.</param>
    /// <param name="position">The incoming LSP line and character.</param>
    /// <returns>The UTF-16 index in the source string.</returns>
    private int Offset(string text, JsonElement position)
    {
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        if (line < 0 || character < 0)
            throw new InvalidDataException("The fixture position is negative.");
        var start = 0;
        for (var i = 0; i < line; i++)
        {
            var next = text.IndexOfAny(['\r', '\n'], start);
            if (next < 0)
                throw new InvalidDataException("The fixture line is outside the document.");
            start = next + (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1);
        }
        var boundary = text.IndexOfAny(['\r', '\n'], start);
        var content = text.AsSpan(start, (boundary < 0 ? text.Length : boundary) - start);
        var offset = start;
        var units = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            if (units == character)
                return offset;
            units += _encoding switch { "utf-8" => rune.Utf8SequenceLength, "utf-32" => 1, _ => rune.Utf16SequenceLength };
            if (units > character)
                throw new InvalidDataException("The fixture position splits a Unicode scalar.");
            offset += rune.Utf16SequenceLength;
        }
        return offset;
    }

    /// <summary>Sends a typed JSON-RPC error to the request's caller.</summary>
    /// <param name="id">The correlated request ID.</param>
    /// <param name="code">The numeric error code.</param>
    /// <param name="message">The error description.</param>
    private void SendError(JsonElement id, int code, string message) => connection.Send(new { jsonrpc = "2.0", id, error = new { code, message } });

    /// <summary>Reads the document identity without translating the URI into a file path.</summary>
    /// <param name="parameters">A document-scoped request or notification.</param>
    /// <returns>The exact URI sent by the client.</returns>
    private static string DocumentUri(JsonElement parameters) => parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;

    /// <summary>Stores the client-supplied document text and version.</summary>
    private sealed record OpenDocument(int Version, string Text);

    /// <summary>Holds a completion response while later input changes or cancels its source.</summary>
    private sealed record PendingCompletion(JsonElement Id, string Uri, OpenDocument Document, int Offset, bool Cancelled = false);
}
