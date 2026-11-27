using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal interface ILanguageRegistration
{
    WorkspaceExplorer Explorer { get; }
    LanguageServerWorkspace LanguageServers { get; }
    LanguageDocument? ReadLanguageDocument(DocumentContext context);
    void ReportLanguageFailure(Exception error, string action);
}

internal sealed class LanguageServerFeatures
{
    internal sealed record ConfigureInput(Guid RootId, string Executable, LanguageAssociation[] Languages,
        string[]? Arguments = null, JsonElement? InitializationOptions = null, JsonElement? Settings = null);
    internal sealed record RootInput(Guid RootId);
    internal sealed record CompleteInput(DocumentContext Document, int Offset);
    internal sealed record DocumentInput(DocumentContext Document);

    internal Feature<ConfigureInput> Configure { get; } = new("language.server.configure", "language_configure_server",
        "Configure and start an installed language server for a workspace root.",
        ConfigureAsync, typeof(LanguageServerStatus), mutation: true);

    internal Feature<GetContextInput> List { get; } = new("language.server.list", "language_list_servers",
        "List configured language servers, process state, and negotiated position encodings.", false,
        static (_, view) => view is ILanguageRegistration registration
            ? FeatureOutcome.Success("Language server state read.", registration.LanguageServers.List())
            : Unavailable(), typeof(LanguageServerStatus[]));

    internal Feature<RootInput> Restart { get; } = new("language.server.restart", "language_restart_server",
        "Restart a configured language server and reopen the live editor buffers.",
        static (input, view, _) => PrepareControl(input, view, running: true), typeof(LanguageServerStatus), mutation: true);

    internal Feature<RootInput> Stop { get; } = new("language.server.stop", "language_stop_server",
        "Stop a configured language server and retain the root configuration.",
        static (input, view, _) => PrepareControl(input, view, running: false), typeof(LanguageServerStatus), mutation: true);

    internal Feature<RootInput> Output { get; } = new("language.server.output", "language_get_server_output",
        "Read the retained standard error of the language server for the specified root.", false,
        static (input, view) => view is ILanguageRegistration registration && RootExists(registration, input.RootId, out _) &&
            registration.LanguageServers.ReadOutput(input.RootId) is { } output
            ? FeatureOutcome.Success("Language server output read.", output) : Unavailable(), typeof(LanguageServerOutput));

    internal Feature<CompleteInput> Complete { get; } = new("language.document.complete", "language_complete",
        "Request completions at a UTF-16 editor offset in the live document revision.",
        static (input, view, token) => QueryAsync(input.Document, input.Offset, "textDocument/completion", "completionProvider", view, token),
        typeof(LanguageQueryResult));

    internal Feature<DocumentInput> Symbols { get; } = new("language.document.symbols", "language_document_symbols",
        "List the document symbols advertised for the live unsaved document revision.",
        static (input, view, token) => QueryAsync(input.Document, null, "textDocument/documentSymbol", "documentSymbolProvider", view, token),
        typeof(LanguageQueryResult));

    internal Feature<DocumentInput> Diagnostics { get; } = new("language.document.diagnostics", "language_get_diagnostics",
        "Read language server diagnostics for the specified live document revision.", false,
        static (input, view) => view is ILanguageRegistration registration && registration.ReadLanguageDocument(input.Document) is { } document
            ? FeatureOutcome.Success("Language server diagnostics read.", registration.LanguageServers.Diagnostics(document))
            : DocumentChanged(), typeof(LanguageDiagnosticResult));

    private static Task<FeatureOutcome> ConfigureAsync(ConfigureInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (view is not ILanguageRegistration registration || !RootExists(registration, input.RootId, out var root))
            return Task.FromResult(RootUnavailable());
        if (!Valid(input))
            return Task.FromResult(new FeatureOutcome("invalid_language_configuration", "Use an installed executable and valid root language associations."));
        var configuration = new LanguageServerConfiguration(root.Id, root.Path, input.Executable, input.Arguments ?? [],
            input.Languages, input.InitializationOptions, input.Settings);
        return PrepareRoot(root, async (current, cancellation) =>
        {
            var status = await current.LanguageServers.ConfigureAsync(configuration, cancellation).ConfigureAwait(false);
            return FeatureOutcome.Success("Language server configured.", status);
        });
    }

    private static Task<FeatureOutcome> PrepareControl(RootInput input, IWorkbenchRegistration view, bool running)
    {
        if (view is not ILanguageRegistration registration || !RootExists(registration, input.RootId, out var root))
            return Task.FromResult(RootUnavailable());
        if (!registration.LanguageServers.Contains(root.Id))
            return Task.FromResult(Unavailable());
        return PrepareRoot(root, async (current, token) =>
        {
            var status = await current.LanguageServers.SetRunningAsync(root.Id, running, token).ConfigureAwait(false);
            return status is not null
                ? FeatureOutcome.Success(running ? "Language server restarted." : "Language server stopped.", status)
                : Unavailable();
        });
    }

    private static Task<FeatureOutcome> PrepareRoot(WorkspaceExplorer.Root root,
        Func<ILanguageRegistration, CancellationToken, Task<FeatureOutcome>> action) =>
        Task.FromResult(new FeatureOutcome("ok", "Language server operation prepared.")
        {
            Commit = current => current is ILanguageRegistration active && active.Explorer.Roots.Contains(root)
                ? new FeatureOutcome("ok", "Language server operation authorized.")
                {
                    ExecuteAuthorizedAsync = token => RunAsync(active, action, token),
                }
                : RootUnavailable(),
        });

    private static async Task<FeatureOutcome> RunAsync(ILanguageRegistration registration,
        Func<ILanguageRegistration, CancellationToken, Task<FeatureOutcome>> action, CancellationToken token)
    {
        try { return await action(registration, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            registration.ReportLanguageFailure(error, "Manage language server process");
            return new("language_server_failed", "The language server could not complete the lifecycle operation.");
        }
    }

    private static async Task<FeatureOutcome> QueryAsync(DocumentContext context, int? offset, string method, string provider,
        IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not ILanguageRegistration registration || registration.ReadLanguageDocument(context) is not { } document)
            return DocumentChanged();
        try
        {
            if (!registration.LanguageServers.TryQuery(document, method, provider, offset, token, out var pending))
                return Unavailable();
            var result = await pending.ConfigureAwait(false);
            return new FeatureOutcome("ok", "Language server result prepared.")
            {
                Commit = current => current is ILanguageRegistration active && active.ReadLanguageDocument(context) is not null &&
                    active.LanguageServers.IsCurrent(context.RootId, result.Server.SessionId)
                    ? FeatureOutcome.Success("Language server result read.", result) : DocumentChanged(),
            };
        }
        catch (LanguageDocumentChangedException) { return DocumentChanged(); }
        catch (LspRemoteException error) when (error.Code is -32800 or -32801) { return DocumentChanged(); }
        catch (ArgumentOutOfRangeException) { return new("invalid_language_position", "Use an editor offset on a character boundary."); }
        catch (NotSupportedException) { return new("language_feature_unavailable", "This language server does not support the requested document operation."); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            registration.ReportLanguageFailure(error, "Query language server");
            return new("language_server_failed", "The language server could not complete the request.");
        }
    }

    private static bool RootExists(ILanguageRegistration registration, Guid rootId, out WorkspaceExplorer.Root root)
    {
        root = registration.Explorer.Roots.FirstOrDefault(item => item.Id == rootId)!;
        return root is not null;
    }

    private static bool Valid(ConfigureInput input) =>
        Path.IsPathFullyQualified(input.Executable) && File.Exists(input.Executable) &&
        (input.Arguments is null || input.Arguments.Length <= 64 && input.Arguments.All(arg => arg is not null && !arg.Contains('\0'))) &&
        input.Languages is { Length: > 0 and <= 32 } &&
        input.Languages.All(language => language is not null && !string.IsNullOrWhiteSpace(language.LanguageId) &&
            language.Extensions is { Length: > 0 and <= 32 } && language.Extensions.All(extension =>
                extension is { Length: > 1 and <= 32 } && extension[0] == '.' && !extension.Contains('/') && !extension.Contains('\\'))) &&
        input.Languages.SelectMany(language => language.Extensions).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
            input.Languages.Sum(language => language.Extensions.Length);

    private static FeatureOutcome RootUnavailable() => new("root_unavailable", "The selected workspace root is unavailable.");
    private static FeatureOutcome Unavailable() => new("language_server_unavailable", "No running language server matches the selected document or root.");
    private static FeatureOutcome DocumentChanged() => new("document_changed", "The source document or its language server session changed.");
}
