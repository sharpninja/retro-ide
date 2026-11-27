using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Source = RetroIde.Application.Emulators.EmulatorBucketCatalog.Source;

namespace RetroIde.Application.Emulators;

/// <summary>Composes the local emulator catalog used by authorized workbench operations.</summary>
internal sealed class EmulatorWorkspace(string root, ILogger logger)
{
    private static readonly Version AdapterApi = new(1, 0);
    private readonly EmulatorBucketConfiguration _configuration = new(root);
    private readonly EmulatorBucketCatalog _catalog = new(root);

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
        var source = _configuration.Read().SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("The emulator bucket is not configured.");
        var snapshot = await _catalog.RefreshAsync(source, token).ConfigureAwait(false);
        return Project(source, snapshot);
    }

    internal async Task<BucketChange> RemoveAsync(string id, CancellationToken token)
    {
        await _configuration.RemoveAsync(id, token).ConfigureAwait(false);
        return new BucketChange(id);
    }

    internal void ReportFailure(Exception error) => RuntimeFailures.Report(logger, error, "Emulator bucket operation", LogLevel.Warning);

    private static BucketState Project(Source source, EmulatorBucketCatalog.Snapshot? snapshot) =>
        new(source.Id, source.Repository.GetLeftPart(UriPartial.Path), snapshot?.Revision,
            snapshot?.Releases.Select(static release => new ReleaseState(release.Pin, release.Manifest.Name,
                release.Manifest.Description, release.Manifest.AdapterId,
                release.Manifest.Targets.ContainsKey(RuntimeInformation.RuntimeIdentifier) &&
                release.Manifest.AdapterApi.Contains(AdapterApi))).ToArray() ?? []);

    internal sealed record CatalogState(string RuntimeIdentifier, string AdapterApi, BucketState[] Buckets);
    internal sealed record BucketState(string Id, string Repository, string? Revision, ReleaseState[] Releases);
    internal sealed record ReleaseState(EmulatorBundleStore.Pin Pin, string Name, string Description, string AdapterId, bool IsCompatible);
    internal sealed record BucketChange(string BucketId);
}
