using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageAssociation(string LanguageId, string[] Extensions);

internal sealed partial record LanguageServerConfiguration(Guid RootId, string Directory, string Executable, string[] Arguments,
    LanguageAssociation[] Languages, JsonElement? InitializationOptions, JsonElement? Settings,
    string ProfileId = LanguageProfileKey.DefaultProfile)
{
    internal string? GetLanguage(string path) => MatchExtension(Path.GetExtension(path));

    private string? MatchExtension(string extension) =>
        Languages.FirstOrDefault(item => item.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))?.LanguageId;
}

internal sealed record LanguageDocument(DocumentContext Context, string Path, string Text, long SavedRevision, string SavedText);
internal sealed record LanguageServerStatus(Guid RootId, string Executable, string State, Guid? SessionId = null,
    string? PositionEncoding = null, int? ProcessId = null, string? Name = null,
    string ProfileId = LanguageProfileKey.DefaultProfile, Guid[]? WorkspaceRootIds = null);
internal sealed record LanguageQueryResult(DocumentContext Document, LanguageServerStatus Server, JsonElement Result, Guid? CompletionOfferId = null)
{
    internal LanguageDocument[]? CapturedBuffers { get; init; }
}
internal sealed record LanguageDiagnosticResult(DocumentContext Document, Guid? SessionId, JsonElement[] Items);
internal sealed record LanguageServerOutput(LanguageServerStatus Server, string StandardError);

internal sealed class LanguageDocumentChangedException() : InvalidOperationException("The language request's source document changed.");

internal sealed partial class LanguageServerSession : IAsyncDisposable
{
    private readonly LanguageServerConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly Action _diagnosticsChanged;
    private readonly LspProcess _rpc;
    private readonly SemaphoreSlim _updates = new(1);
    private readonly Lock _closeGate = new();
    private readonly ConcurrentDictionary<string, OpenDocument> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DiagnosticSet> _diagnostics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _registrations = new(StringComparer.Ordinal);
    private readonly string _rootUri;
    private Task? _close;
    private bool _initialized;
    private bool _openClose;
    private bool _save;
    private bool _includeSaveText;
    private int _change;
    private JsonElement _capabilities;

    private LanguageServerSession(LanguageServerConfiguration[] roots, Process process, ILogger logger, Action diagnosticsChanged)
    {
        _configuration = roots[0];
        _logger = logger;
        _diagnosticsChanged = diagnosticsChanged;
        _rootUri = FileUri(_configuration.Directory);
        _folders = roots.Select(Folder.For).ToArray();
        _rpc = new LspProcess(process, logger, OnNotification, OnRequest);
    }

    internal Guid Id { get; } = Guid.NewGuid();
    internal string Encoding { get; private set; } = "utf-16";
    internal string? Name { get; private set; }
    internal bool IsRunning => Volatile.Read(ref _close) is null && _rpc.IsRunning;
    internal string Output => _rpc.Output;
    internal Task Completion => _rpc.Completion;

    internal LanguageServerStatus StatusFor(LanguageServerConfiguration configuration) => new(configuration.RootId, configuration.Executable,
        IsRunning ? "running" : "faulted", Id, Encoding, _rpc.ProcessId, Name, configuration.ProfileId,
        Volatile.Read(ref _folders).Select(folder => folder.RootId).ToArray());

    internal static async Task<LanguageServerSession> StartAsync(LanguageServerConfiguration[] roots, ILogger logger,
        Action diagnosticsChanged, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var configuration = roots[0];
        var start = new ProcessStartInfo(configuration.Executable)
        {
            WorkingDirectory = configuration.Directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in configuration.Arguments)
            start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new IOException("The language server did not start.");
        var session = new LanguageServerSession(roots, process, logger, diagnosticsChanged);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await session.InitializeAsync(deadline.Token).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        var reply = await _rpc.RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = _rootUri,
            workspaceFolders = Folders(),
            clientInfo = new { name = "Retro IDE" },
            initializationOptions = _configuration.InitializationOptions,
            capabilities = new
            {
                general = new { positionEncodings = new[] { "utf-8", "utf-16", "utf-32" } },
                workspace = new
                {
                    configuration = true, workspaceFolders = true,
                    didChangeConfiguration = new { dynamicRegistration = true },
                    workspaceEdit = new { documentChanges = true },
                },
                textDocument = new
                {
                    completion = new { completionItem = new { insertReplaceSupport = true } },
                    hover = new { contentFormat = new[] { "plaintext", "markdown" } },
                    definition = new { linkSupport = true },
                    declaration = new { linkSupport = true },
                    typeDefinition = new { linkSupport = true },
                    implementation = new { linkSupport = true },
                    codeAction = new
                    {
                        disabledSupport = true,
                        codeActionLiteralSupport = new { codeActionKind = new { valueSet = new[] { "", "quickfix", "refactor", "source" } } },
                        resolveSupport = new { properties = new[] { "edit" } },
                    },
                    publishDiagnostics = new { versionSupport = true },
                    documentSymbol = new { hierarchicalDocumentSymbolSupport = true },
                },
            },
        }, token).ConfigureAwait(false);
        _capabilities = reply.GetProperty("capabilities");
        Encoding = _capabilities.TryGetProperty("positionEncoding", out var position) ? position.GetString() ?? "utf-16" : "utf-16";
        if (Encoding is not ("utf-8" or "utf-16" or "utf-32"))
            throw new InvalidDataException("The language server did not negotiate a supported position encoding.");
        if (reply.TryGetProperty("serverInfo", out var serverInfo) && serverInfo.TryGetProperty("name", out var name))
            Name = name.GetString();
        ReadSynchronization(_capabilities);
        ReadFolderSupport(_capabilities);
        await _rpc.NotifyAsync("initialized", new { }, token).ConfigureAwait(false);
        _initialized = true;
    }

    private void ReadSynchronization(JsonElement capabilities)
    {
        if (!capabilities.TryGetProperty("textDocumentSync", out var sync))
            return;
        if (sync.ValueKind == JsonValueKind.Number)
        {
            _change = sync.GetInt32();
            _openClose = _change != 0;
        }
        else if (sync.ValueKind == JsonValueKind.Object)
        {
            _openClose = TryTrue(sync, "openClose");
            _change = sync.TryGetProperty("change", out var change) ? change.GetInt32() : 0;
            if (sync.TryGetProperty("save", out var save))
            {
                _save = save.ValueKind is JsonValueKind.Object or JsonValueKind.True;
                _includeSaveText = save.ValueKind == JsonValueKind.Object && TryTrue(save, "includeText");
            }
        }
        if (_change is < 0 or > 2)
            throw new InvalidDataException("The language server advertised an unknown document synchronization mode.");
    }

    internal bool Supports(string method, string provider)
    {
        if (_registrations.Values.Contains(method, StringComparer.Ordinal))
            return true;
        if (_capabilities.ValueKind != JsonValueKind.Object || !_capabilities.TryGetProperty(provider, out var value))
            return false;
        return method is "codeAction/resolve" or "completionItem/resolve"
            ? value.ValueKind == JsonValueKind.Object && TryTrue(value, "resolveProvider")
            : value.ValueKind is JsonValueKind.True or JsonValueKind.Object;
    }

    internal async Task<LanguageQueryResult> QueryAsync(LanguageDocument document, LanguageServerConfiguration configuration,
        string language, LanguageRequest query, Func<bool> currentDocument, CancellationToken token)
    {
        if (!Supports(query.Method, query.Provider))
            throw new NotSupportedException("The language server does not advertise this feature.");

        var uri = FileUri(document.Path);
        Task<JsonElement> request;
        CancellationTokenSource linked;
        CancellationToken revisionToken;
        await _updates.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!ContainsRoot(document.Context.RootId) || !currentDocument())
                throw new LanguageDocumentChangedException();
            var tracked = await SynchronizeCoreAsync(uri, document, language, token).ConfigureAwait(false);
            var arguments = query.Build(uri, tracked.Text, Encoding);
            revisionToken = tracked.Requests.Token;
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, revisionToken);
            linked.CancelAfter(TimeSpan.FromSeconds(20));
            request = _rpc.RequestAsync(query.Method, arguments, linked.Token);
        }
        finally { _updates.Release(); }

        using (linked)
        {
            try
            {
                var result = await request.ConfigureAwait(false);
                if (!_rpc.IsRunning)
                    throw new IOException("The language server disconnected during the document request.");
                if (!IsRunning || !_documents.TryGetValue(uri, out var current) || current.Context != document.Context)
                    throw new LanguageDocumentChangedException();
                return new LanguageQueryResult(document.Context, StatusFor(configuration), result);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !_rpc.IsRunning)
            {
                throw new IOException("The language server disconnected during the document request.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && revisionToken.IsCancellationRequested)
            {
                throw new LanguageDocumentChangedException();
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && linked.IsCancellationRequested)
            {
                throw new TimeoutException("The language server did not answer the document request in time.");
            }
        }
    }

    internal async Task SynchronizeAsync(LanguageDocument document, string language, Func<bool> currentDocument, CancellationToken token)
    {
        await _updates.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (ContainsRoot(document.Context.RootId) && currentDocument())
                _ = await SynchronizeCoreAsync(FileUri(document.Path), document, language, token).ConfigureAwait(false);
        }
        finally { _updates.Release(); }
    }

    internal async Task CloseDocumentAsync(LanguageDocument document, CancellationToken token)
    {
        await _updates.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var uri = FileUri(document.Path);
            if (_documents.TryGetValue(uri, out var tracked) && tracked.Context.DocumentId == document.Context.DocumentId)
                await CloseCoreAsync(uri, token).ConfigureAwait(false);
        }
        finally { _updates.Release(); }
    }

    internal LanguageDiagnosticResult Diagnostics(LanguageDocument document)
    {
        var uri = FileUri(document.Path);
        return new LanguageDiagnosticResult(document.Context, Id,
            _diagnostics.TryGetValue(uri, out var found) && found.Context == document.Context ? found.Items : []);
    }

    internal int? ReadDocumentVersion(LanguageDocument document) =>
        _documents.TryGetValue(FileUri(document.Path), out var open) && open.Context == document.Context ? open.Version : null;

    public ValueTask DisposeAsync()
    {
        lock (_closeGate)
            return new ValueTask(_close ??= CloseAsync());
    }

    private async Task CloseAsync()
    {
        await _updates.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var document in _documents.Values)
                document.CancelRequests();
            _documents.Clear();
            if (_initialized && _rpc.IsRunning)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _rpc.ExitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Shut down language server", LogLevel.Warning);
        }
        finally
        {
            _diagnostics.Clear();
            try { await _rpc.DisposeAsync().ConfigureAwait(false); }
            finally { _updates.Release(); }
        }
    }

    private static bool TryTrue(JsonElement value, string property) =>
        value.TryGetProperty(property, out var member) && member.ValueKind == JsonValueKind.True;

    internal static string FileUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private sealed record OpenDocument(DocumentContext Context, int Version, string Text, long SavedRevision)
    {
        internal CancellationTokenSource Requests { get; } = new();

        internal void CancelRequests()
        {
            try { Requests.Cancel(); }
            finally { Requests.Dispose(); }
        }
    }
    private sealed record DiagnosticSet(DocumentContext Context, JsonElement[] Items);
}
