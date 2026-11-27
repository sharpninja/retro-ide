using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RetroIde.Application.LanguageServers;

internal sealed class LspRemoteException(int code) : IOException($"Language server returned JSON-RPC error {code}.")
{
    internal int Code => code;
}

internal sealed class LspProcess : IAsyncDisposable
{
    private const int OutputLimit = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _requests = new();
    private readonly Lock _disposeGate = new();
    private readonly Lock _outputGate = new();
    private readonly StringBuilder _output = new();
    private readonly Task _reader;
    private readonly Task _errors;
    private readonly Action<string, JsonElement> _notify;
    private readonly Func<string, JsonElement, (bool Handled, object? Result)> _request;
    private readonly Func<JsonElement, CancellationToken, Task<LanguageServerEditReply>> _reviewEdit;
    private Task _serverEdit = Task.CompletedTask;
    private CancellationTokenSource? _serverEditCancellation;
    private JsonElement _serverEditId;
    private long _nextId;
    private Task? _disposed;

    internal LspProcess(Process process, ILogger logger, Action<string, JsonElement> notify,
        Func<string, JsonElement, (bool Handled, object? Result)> request,
        Func<JsonElement, CancellationToken, Task<LanguageServerEditReply>> reviewEdit)
    {
        _process = process;
        _logger = logger;
        _notify = notify;
        _request = request;
        _reviewEdit = reviewEdit;
        ProcessId = process.Id;
        _reader = Task.Run(ReadMessagesAsync);
        _errors = Task.Run(ReadErrorsAsync);
    }

    internal int ProcessId { get; }
    internal bool IsRunning => !_lifetime.IsCancellationRequested && !_reader.IsCompleted;
    internal Task Completion => _reader;

    internal string Output
    {
        get { lock (_outputGate) return _output.ToString(); }
    }

    internal async Task<JsonElement> RequestAsync(string method, object? arguments, CancellationToken token)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.TryAdd(id, completion))
            throw new InvalidOperationException("The language server request ID was already in use.");
        var sent = false;
        try
        {
            var envelope = new Dictionary<string, object?>(StringComparer.Ordinal) { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (arguments is not null)
                envelope["params"] = arguments;
            await WriteAsync(envelope, token).ConfigureAwait(false);
            sent = true;
            return await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (sent)
                _ = CancelRequestAsync(id);
            throw;
        }
        finally { _requests.TryRemove(id, out _); }
    }

    internal Task NotifyAsync(string method, object arguments, CancellationToken token) =>
        WriteAsync(new { jsonrpc = "2.0", method, @params = arguments }, token);

    internal async Task ExitAsync(CancellationToken token)
    {
        _ = await RequestAsync("shutdown", null, token).ConfigureAwait(false);
        await WriteAsync(new { jsonrpc = "2.0", method = "exit" }, token).ConfigureAwait(false);
        await _process.WaitForExitAsync(token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposed ??= StopAsync());
    }

    private async Task WriteAsync<T>(T message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_lifetime.IsCancellationRequested)
            throw new IOException("The language server connection is closed.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (bytes.Length > LspFrameReader.MaximumBodyBytes)
            throw new InvalidDataException("The language server message exceeded the byte limit.");
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _writeGate.WaitAsync(wait.Token).ConfigureAwait(false);
        var writingFrame = false;
        try
        {
            token.ThrowIfCancellationRequested();
            // Once a frame begins, finish it or close the connection. Cancelling a partial frame would desynchronize the peer.
            using var frame = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            frame.CancelAfter(TimeSpan.FromSeconds(5));
            var output = _process.StandardInput.BaseStream;
            writingFrame = true;
            await output.WriteAsync(header, frame.Token).ConfigureAwait(false);
            await output.WriteAsync(bytes, frame.Token).ConfigureAwait(false);
            await output.FlushAsync(frame.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (writingFrame)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            throw new IOException("Writing to the language server stopped before the frame was sent.");
        }
        finally { _writeGate.Release(); }
    }

    private async Task ReadMessagesAsync()
    {
        var frames = new LspFrameReader(_process.StandardOutput.BaseStream);
        try
        {
            while (await frames.ReadAsync(_lifetime.Token).ConfigureAwait(false) is { } message)
            {
                using (message)
                    await DispatchAsync(message.RootElement).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Read language server protocol");
        }
        finally
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            foreach (var pending in _requests.Values)
                pending.TrySetException(new IOException("The language server disconnected."));
        }
    }

    private async Task DispatchAsync(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out var version) ||
            version.ValueKind != JsonValueKind.String || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal))
            throw new InvalidDataException("The language server sent an invalid JSON-RPC message.");
        message.TryGetProperty("id", out var id);
        if (message.TryGetProperty("method", out var name))
        {
            var method = name.GetString() ?? throw new InvalidDataException("The language server method has no name.");
            message.TryGetProperty("params", out var parameters);
            if (id.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            {
                if (string.Equals(method, "workspace/applyEdit", StringComparison.Ordinal))
                    await StartServerEditAsync(id, parameters).ConfigureAwait(false);
                else
                    await RespondAsync(method, id, parameters).ConfigureAwait(false);
            }
            else if (string.Equals(method, "$/cancelRequest", StringComparison.Ordinal) &&
                parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("id", out var pendingId) &&
                !_serverEdit.IsCompleted && JsonElement.DeepEquals(_serverEditId, pendingId))
            {
                await CancelServerEditAsync().ConfigureAwait(false);
            }
            else
                _notify(method, parameters);
        }
        else if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && _requests.TryGetValue(number, out var pending))
        {
            if (message.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) && code.TryGetInt32(out var errorCode))
                pending.TrySetException(new LspRemoteException(errorCode));
            else if (message.TryGetProperty("result", out var result))
                pending.TrySetResult(result.Clone());
            else
                pending.TrySetException(new InvalidDataException("The language server reply has no result or error."));
        }
    }

    private Task RespondAsync(string method, JsonElement id, JsonElement parameters)
    {
        var (handled, result) = _request(method, parameters);
        if (handled)
            return WriteAsync(new { jsonrpc = "2.0", id, result }, _lifetime.Token);

        var (code, message) = method is "client/registerCapability" or "client/unregisterCapability"
            ? (-32602, "The capability registration is invalid or unsupported.")
            : (-32601, "The client method is unavailable.");
        return WriteAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, _lifetime.Token);
    }

    private Task StartServerEditAsync(JsonElement id, JsonElement parameters)
    {
        if (!_serverEdit.IsCompleted)
            return WriteAsync(new { jsonrpc = "2.0", id, result = new LanguageServerEditReply(false, "Another edit from this server is pending.") },
                _lifetime.Token);

        var request = parameters.ValueKind == JsonValueKind.Undefined ? default : parameters.Clone();
        _serverEditId = id.Clone();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _serverEditCancellation = cancellation;
        var requestId = _serverEditId;
        // Keep the pipe reader available for replies, diagnostics and shutdown while the user reviews this request.
        _serverEdit = Task.Run(() => RespondToServerEditAsync(requestId, request, cancellation));
        return Task.CompletedTask;
    }

    private async Task RespondToServerEditAsync(JsonElement id, JsonElement request, CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            try
            {
                var result = await _reviewEdit(request, cancellation.Token).ConfigureAwait(false);
                await WriteAsync(new { jsonrpc = "2.0", id, result }, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error) && _lifetime.IsCancellationRequested) { }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(_logger, error, "Respond to language server workspace edit");
            }
        }
    }

    private async Task CancelServerEditAsync()
    {
        try
        {
            if (_serverEditCancellation is { } pending)
                await pending.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }
    }

    private async Task CancelRequestAsync(long id)
    {
        try { await NotifyAsync("$/cancelRequest", new { id }, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error) && _lifetime.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Cancel language server request");
        }
    }

    private async Task ReadErrorsAsync()
    {
        var buffer = new char[2048];
        try
        {
            while (await _process.StandardError.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false) is > 0 and var count)
            {
                lock (_outputGate)
                {
                    _output.Append(buffer, 0, count);
                    if (_output.Length > OutputLimit)
                        _output.Remove(0, _output.Length - OutputLimit);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Read language server output");
        }
    }

    private async Task StopAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (_process.HasExited) { }
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(_reader, _errors).WaitAsync(deadline.Token).ConfigureAwait(false);
            await _serverEdit.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally { _process.Dispose(); }
    }
}
