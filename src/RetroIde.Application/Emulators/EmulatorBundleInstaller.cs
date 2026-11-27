namespace RetroIde.Application.Emulators;

/// <summary>Installs the host artifact declared at an exact Git revision.</summary>
/// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction.</remarks>
internal sealed class EmulatorBundleInstaller(EmulatorBucketCatalog catalog, EmulatorBundleStore store, HttpClient http)
{
    internal async Task<EmulatorBundleStore.InstalledBundle> InstallAsync(EmulatorBucketCatalog.Source source,
        EmulatorBundleStore.Pin pin, string runtimeIdentifier, Version adapterApi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pin = pin.Normalized();
        if (!string.Equals(pin.Bucket, source.Id, StringComparison.Ordinal))
            throw new ArgumentException("The selected pin belongs to another Git bucket.", nameof(pin));
        if (store.Resolve(pin, runtimeIdentifier, adapterApi) is { } installed)
            return installed;

        var release = await catalog.ReadPinnedAsync(source, pin, allowFetch: true, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected bundle was not found at its pinned Git revision.");
        var artifact = release.Manifest.TargetFor(runtimeIdentifier, adapterApi);
        if (artifact.Url.IsFile)
        {
            if (!source.Repository.IsFile)
                throw new InvalidDataException("A Git bucket on the network cannot install an artifact from a local file.");
            var file = new FileStream(artifact.Url.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (file.ConfigureAwait(false))
                return await store.InstallAsync(pin, runtimeIdentifier, adapterApi, release.ManifestBytes, file, cancellationToken).ConfigureAwait(false);
        }

        using var response = await http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            return await store.InstallAsync(pin, runtimeIdentifier, adapterApi, release.ManifestBytes, stream, cancellationToken).ConfigureAwait(false);
    }
}
