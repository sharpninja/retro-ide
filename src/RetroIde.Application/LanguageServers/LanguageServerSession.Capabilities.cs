using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerSession
{
    private const int MaximumRegistrations = 128;
    private const string SettingsChanged = "workspace/didChangeConfiguration";
    private CapabilityRegistration[] _registrations = [];
    private CapabilityRegistration[] _staticDocumentCapabilities = [];

    private LanguageDocumentCapabilities ReadDocumentCapabilities(string language, string path, string root)
    {
        var dynamic = Volatile.Read(ref _registrations);
        var methods = LspDocumentMethods.Providers.Keys.Where(method => Supports(method, language, path, root, dynamic)).ToList();
        if (Supports(LspDocumentMethods.CodeActionResolve, language, path, root, dynamic))
            methods.Add(LspDocumentMethods.CodeActionResolve);
        methods.Add(LspDocumentMethods.Diagnostics);
        return new(Id, methods.ToArray());
    }

    internal LanguageDocumentCapabilities DocumentCapabilities(LanguageServerConfiguration configuration, string language, string path) =>
        ReadDocumentCapabilities(language, path, configuration.Directory);

    private bool Supports(string method, string language, string path, string root) =>
        Supports(method, language, path, root, Volatile.Read(ref _registrations));

    private bool Supports(string method, string language, string path, string root, CapabilityRegistration[] dynamic)
    {
        var parent = method switch
        {
            LspDocumentMethods.CompletionResolve => LspDocumentMethods.Completion,
            LspDocumentMethods.CodeActionResolve => LspDocumentMethods.CodeAction,
            _ => method,
        };
        var resolve = !string.Equals(parent, method, StringComparison.Ordinal);
        return _staticDocumentCapabilities.Any(Matches) || dynamic.Any(Matches);

        bool Matches(CapabilityRegistration candidate) =>
            string.Equals(candidate.Method, parent, StringComparison.Ordinal) && (!resolve || candidate.ResolveProvider) &&
            candidate.Selector.Matches(language, path, root);
    }

    private void ReadStaticDocumentCapabilities(JsonElement capabilities)
    {
        var providers = new List<CapabilityRegistration>();
        foreach (var (method, provider) in LspDocumentMethods.Providers)
        {
            if (!capabilities.TryGetProperty(provider, out var options) || options.ValueKind is not (JsonValueKind.Object or JsonValueKind.True))
                continue;
            try { providers.Add(CapabilityRegistration.Read(provider, method, options)); }
            catch (Exception error) when (error is FormatException or ArgumentException or NotSupportedException)
            {
                RuntimeFailures.Report(_logger, error, "Read static language server document selector");
            }
        }
        _staticDocumentCapabilities = providers.ToArray();
    }

    private bool IsRegistered(string method) => Volatile.Read(ref _registrations)
        .Any(registration => string.Equals(registration.Method, method, StringComparison.Ordinal));

    private bool ChangeRegistration(JsonElement parameters, bool remove)
    {
        var key = remove ? "unregisterations" : "registrations";
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(key, out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > MaximumRegistrations)
            return false;

        var current = Volatile.Read(ref _registrations);
        try
        {
            var changes = choices.EnumerateArray().Select(item => ParseChange(item, remove)).ToArray();
            if (changes.Select(change => change.Id).Distinct(StringComparer.Ordinal).Count() != changes.Length)
                return false;

            var next = remove ? RemoveRegistrations(current, changes) : AddRegistrations(current, changes);
            if (next is null)
                return false;
            Volatile.Write(ref _registrations, next);
            _stateChanged();
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or NotSupportedException)
        {
            RuntimeFailures.Report(_logger, error, "Validate language server capability registration");
            return false;
        }
    }

    private static CapabilityRegistration ParseChange(JsonElement item, bool remove)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 and <= 256 } name ||
            !item.TryGetProperty("method", out var member) || member.ValueKind != JsonValueKind.String ||
            member.GetString() is not { } method ||
            (method is not (SettingsChanged or FolderChange) && !LspDocumentMethods.Providers.ContainsKey(method)))
            throw new FormatException("The language server requested an unsupported capability registration.");

        item.TryGetProperty("registerOptions", out var options);
        if (!remove && options.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            throw new FormatException("Invalid language server registration options.");
        return CapabilityRegistration.Read(name, method, remove ? default : options);
    }

    private static CapabilityRegistration[]? AddRegistrations(CapabilityRegistration[] current, CapabilityRegistration[] additions)
    {
        var existing = current.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return current.Length + additions.Length > MaximumRegistrations || additions.Any(item => existing.Contains(item.Id))
            ? null : [.. current, .. additions];
    }

    private CapabilityRegistration[]? RemoveRegistrations(CapabilityRegistration[] current, CapabilityRegistration[] removals)
    {
        var removing = removals.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (current.Any(item => removing.TryGetValue(item.Id, out var removed) &&
            !string.Equals(item.Method, removed.Method, StringComparison.Ordinal)))
            return null;

        if (_folderRegistrationId is { } id && removing.TryGetValue(id, out var folder) &&
            string.Equals(folder.Method, FolderChange, StringComparison.Ordinal))
            _folderRegistrationId = null;
        return current.Where(item => !removing.ContainsKey(item.Id)).ToArray();
    }

    private sealed record CapabilityRegistration(string Id, string Method, LspDocumentSelector Selector, bool ResolveProvider)
    {
        internal static CapabilityRegistration Read(string id, string method, JsonElement options) =>
            new(id, method, LspDocumentSelector.Read(options), options.ValueKind == JsonValueKind.Object && TryTrue(options, "resolveProvider"));
    }
}
