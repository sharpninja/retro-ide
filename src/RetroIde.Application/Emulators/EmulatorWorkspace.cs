using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RetroIde.Emulation;
using Source = RetroIde.Application.Emulators.EmulatorBucketCatalog.Source;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;
using Project = RetroIde.Application.Workspaces.WorkspaceExplorer.Root;

namespace RetroIde.Application.Emulators;

/// <summary>Composes the local emulator catalog used by authorized workbench operations.</summary>
internal sealed class EmulatorWorkspace
{
    private static Version AdapterApi => AdapterContract.CurrentVersion;
    private static readonly HttpClient DownloadClient = new();
    private readonly string _root;
    private readonly ILogger _logger;
    private readonly EmulatorBucketConfiguration _configuration;
    private readonly EmulatorBucketCatalog _catalog;
    private readonly EmulatorBundleStore _store;
    private readonly EmulatorBundleInstaller _installer;

    internal EmulatorWorkspace(string root, ILogger logger)
    {
        _root = root;
        _logger = logger;
        _configuration = new(root);
        _catalog = new(root);
        _store = new(root);
        _installer = new(_catalog, _store, DownloadClient);
    }

    internal async Task<CatalogState> ReadAsync(CancellationToken token)
    {
        var buckets = new List<BucketState>();
        foreach (var source in _configuration.Read())
        {
            var snapshot = await _catalog.ReadCachedAsync(source, token).ConfigureAwait(false);
            buckets.Add(Project(source, snapshot));
        }
        return new CatalogState(RuntimeInformation.RuntimeIdentifier, AdapterApi.ToString(), buckets.ToArray());
    }

    internal async Task<BucketState> ConfigureAsync(string id, string repository, CancellationToken token)
    {
        if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri))
            throw new ArgumentException("Use an absolute Git repository URL.", nameof(repository));
        var source = new Source(id, uri);
        await _configuration.ConfigureAsync(source, token).ConfigureAwait(false);
        return Project(source, snapshot: null);
    }

    internal async Task<BucketState> RefreshAsync(string id, CancellationToken token)
    {
        var source = SourceFor(id);
        var snapshot = await _catalog.RefreshAsync(source, token).ConfigureAwait(false);
        return Project(source, snapshot);
    }

    internal async Task<BucketChange> RemoveAsync(string id, CancellationToken token)
    {
        await _configuration.RemoveAsync(id, token).ConfigureAwait(false);
        return new BucketChange(id);
    }

    internal InstalledState[] Installed(CancellationToken token) =>
        _store.Inventory(RuntimeInformation.RuntimeIdentifier, ReportFailure, token).Select(ProjectInstall).ToArray();

    internal async Task<InstalledState> InstallAsync(Pin pin, CancellationToken token)
    {
        pin = Normalize(pin);
        if (_store.Resolve(pin, RuntimeInformation.RuntimeIdentifier, AdapterApi) is { } existing)
            return ProjectInstall(existing);
        return await InstallAsync(SourceFor(pin.Bucket), pin, token).ConfigureAwait(false);
    }

    internal async Task<InstalledState> UpdateAsync(Pin current, Pin target, CancellationToken token)
    {
        current = Normalize(current);
        target = Normalize(target);
        if (!string.Equals(current.Bucket, target.Bucket, StringComparison.Ordinal) ||
            !string.Equals(current.Bundle, target.Bundle, StringComparison.Ordinal) ||
            string.Equals(current.Version, target.Version, StringComparison.Ordinal))
            throw new Rejected("emulator_bundle_update_invalid", "Choose a different catalog release of the same bundle.");
        using var original = _store.Acquire(current, RuntimeInformation.RuntimeIdentifier, AdapterApi)
            ?? throw new Rejected("emulator_bundle_missing", "The original bundle release is not installed.");
        var source = SourceFor(target.Bucket);
        var snapshot = await _catalog.ReadCachedAsync(source, token).ConfigureAwait(false);
        if (snapshot is null || !snapshot.Releases.Any(release => release.Pin == target))
            throw new Rejected("emulator_bundle_update_changed", "Refresh the catalog and select its current bundle release.");
        return await InstallAsync(source, target, token).ConfigureAwait(false);
    }

    internal RemovalState RemoveBundle(Pin pin, CancellationToken token)
    {
        pin = Normalize(pin);
        return _store.Remove(pin, RuntimeInformation.RuntimeIdentifier, token) switch
        {
            EmulatorBundleStore.Removal.InUse => throw new Rejected("emulator_bundle_in_use", "A running session holds this bundle release."),
            EmulatorBundleStore.Removal.Missing => throw new Rejected("emulator_bundle_missing", "This bundle release is not installed."),
            _ => new RemovalState(pin),
        };
    }

    internal SelectionState ReadSelection(Project project) => ProjectSelection(project.Id, Selection(project).Read());

    internal async Task<AdapterResult<PinnedEmulatorSession>> OpenSelectedAsync(Project project, EmulatorSessionPlan plan,
        Func<string, IEmulatorAdapterFactory> activateBinding, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return AdapterResult<PinnedEmulatorSession>.Failure(new(AdapterErrorCodes.Cancelled, "Opening the emulator session was cancelled."));
        Pin? pin;
        try { pin = Selection(project).Read()?.Active; }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            ReportFailure(error);
            return AdapterResult<PinnedEmulatorSession>.Failure(new(AdapterErrorCodes.Failed,
                "The project's emulator selection could not be read. Check the application log for details."));
        }

        if (pin is null)
            return AdapterResult<PinnedEmulatorSession>.Failure(new(AdapterErrorCodes.Unavailable, "Select an emulator bundle for this project."));

        var binding = ResolveBinding(pin);
        if (!binding.Succeeded)
            return AdapterResult<PinnedEmulatorSession>.Failure(binding.Error);
        var coordinator = new EmulatorSessionCoordinator(activateBinding, _logger);
        return await coordinator.OpenAsync(binding.Value, plan, token).ConfigureAwait(false);
    }

    internal async Task<SelectionState> SelectAsync(Project project, Pin pin, bool rollback, CancellationToken token)
    {
        pin = Normalize(pin);
        var selection = Selection(project);
        var result = rollback
            ? await selection.RollbackAsync(pin, RuntimeInformation.RuntimeIdentifier, AdapterApi, token).ConfigureAwait(false)
            : await selection.SelectAsync(pin, RuntimeInformation.RuntimeIdentifier, AdapterApi, token).ConfigureAwait(false);
        return ProjectSelection(project.Id, result);
    }

    private SelectionState ProjectSelection(Guid projectId, EmulatorProjectSelectionStore.Snapshot? selection) =>
        new(projectId, selection, selection?.Active is { } pin ? BindingDetails(pin) : null);

    private ProjectBinding BindingDetails(Pin pin)
    {
        var resolved = ResolveBinding(pin);
        if (!resolved.Succeeded)
            return new(resolved.Error.Code);
        using var binding = resolved.Value;
        var install = binding.Installation;
        return new("ready", install.Manifest.AdapterId, install.Receipt.RuntimeIdentifier, AdapterApi.ToString());
    }

    private AdapterResult<EmulatorBundleStore.BundleLease> ResolveBinding(Pin pin)
    {
        try
        {
            var binding = _store.Acquire(pin, RuntimeInformation.RuntimeIdentifier, AdapterApi);
            return binding is null
                ? AdapterResult<EmulatorBundleStore.BundleLease>.Failure(new(AdapterErrorCodes.Unavailable,
                    "The project's pinned emulator bundle is unavailable on this host."))
                : AdapterResult<EmulatorBundleStore.BundleLease>.Success(binding);
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            ReportFailure(error);
            return AdapterResult<EmulatorBundleStore.BundleLease>.Failure(new(AdapterErrorCodes.Incompatible,
                "The project's pinned emulator binding could not be verified."));
        }
    }

    internal void ReportFailure(Exception error) => RuntimeFailures.Report(_logger, error, "Emulator management operation", LogLevel.Warning);

    private Source SourceFor(string id) => _configuration.Read().SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("The emulator bucket is not configured.");

    private EmulatorProjectSelectionStore Selection(Project project) => new(project.Path, _root, _store);

    private async Task<InstalledState> InstallAsync(Source source, Pin pin, CancellationToken token) =>
        ProjectInstall(await _installer.InstallAsync(source, pin, RuntimeInformation.RuntimeIdentifier, AdapterApi, token).ConfigureAwait(false));

    private static Pin Normalize(Pin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        return pin.Normalized();
    }

    private static InstalledState ProjectInstall(EmulatorBundleStore.InstalledBundle installed) =>
        new(installed.Receipt.Pin, installed.Manifest.Name, installed.Manifest.AdapterId,
            installed.Receipt.RuntimeIdentifier, installed.Manifest.AdapterApi.Contains(AdapterApi));

    private static BucketState Project(Source source, EmulatorBucketCatalog.Snapshot? snapshot) =>
        new(source.Id, source.Repository.GetLeftPart(UriPartial.Path), snapshot?.Revision,
            snapshot?.Releases.Select(static release => new ReleaseState(release.Pin, release.Manifest.Name,
                release.Manifest.Description, release.Manifest.AdapterId,
                release.Manifest.Targets.ContainsKey(RuntimeInformation.RuntimeIdentifier) &&
                release.Manifest.AdapterApi.Contains(AdapterApi))).ToArray() ?? []);

    internal sealed record CatalogState(string RuntimeIdentifier, string AdapterApi, BucketState[] Buckets);
    internal sealed record BucketState(string Id, string Repository,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Revision, ReleaseState[] Releases);
    internal sealed record ReleaseState(EmulatorBundleStore.Pin Pin, string Name, string Description, string AdapterId, bool IsCompatible);
    internal sealed record BucketChange(string BucketId);
    internal sealed record InstalledState(Pin Pin, string Name, string AdapterId, string RuntimeIdentifier, bool IsCompatible);
    internal sealed record RemovalState(Pin Pin);
    internal sealed record ProjectBinding(string Status, string? AdapterId = null, string? RuntimeIdentifier = null, string? ContractVersion = null);
    internal sealed record SelectionState(Guid RootId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] EmulatorProjectSelectionStore.Snapshot? Selection,
        ProjectBinding? Binding = null);

    internal sealed class Rejected(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
