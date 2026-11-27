using System.Text;
using RetroIde.Application.Emulators;
using RetroIde.Testing;
using Xunit;
using Source = RetroIde.Application.Emulators.EmulatorBucketCatalog.Source;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest; TEST-RETRO-009#ac-test-bucket-platforms.</remarks>
    [Fact]
    public async Task A_Git_bucket_resolves_the_exact_committed_manifest_and_installs_the_host_target()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var catalog = new EmulatorBucketCatalog(root.Directory);
        var source = new Source("official", new Uri(git.Remote));
        var runtime = OperatingSystem.IsWindows() ? "win-x64" : RuntimeIdentifier;
        var executable = OperatingSystem.IsWindows() ? "bin/uae.exe" : "bin/uae";
        using var zip = Archive(("adapter/Uae.dll", 0), (executable, 0x81ed), ("profiles/amiga-aga.json", 0));
        var bytes = ReleaseManifest(FirstPin, Digest(zip));
        if (OperatingSystem.IsWindows())
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(WindowsHash, Digest(zip), StringComparison.Ordinal));
        var revision = await PublishManifestAsync(git, bytes, token);

        Assert.Null(await catalog.ReadCachedAsync(source, token));
        var snapshot = await catalog.RefreshAsync(source, token);
        Assert.Equal(revision, snapshot.Revision, StringComparer.Ordinal);
        var release = Assert.Single(snapshot.Releases);
        Assert.Equal(FirstPin with { ManifestRevision = revision }, release.Pin);
        Assert.Equal(bytes, release.ManifestBytes.ToArray());
        Assert.Equal(Digest(zip), release.Manifest.TargetFor(runtime, AdapterApi).Sha256, StringComparer.Ordinal);
        Assert.Throws<InvalidOperationException>(() => release.Manifest.TargetFor("linux-arm64", AdapterApi));
        Assert.Throws<InvalidOperationException>(() => release.Manifest.TargetFor(runtime, new Version(2, 0)));
        var installed = await root.Store.InstallAsync(release.Pin, runtime, AdapterApi, release.ManifestBytes, zip, token);
        Assert.EndsWith("/files/" + executable, installed.EntryPoint("emulator"), StringComparison.Ordinal);
        Assert.Equal(revision, installed.Receipt.Pin.ManifestRevision, StringComparer.Ordinal);
    }

    /// <remarks>FR-RETRO-009#ac-offline-bundles; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Invalid_or_unreachable_updates_preserve_the_last_catalog_and_local_installation()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var source = new Source("official", new Uri(git.Remote));
        var catalog = new EmulatorBucketCatalog(root.Directory);
        using var zip = LinuxReleaseArchive();
        await PublishManifestAsync(git, ReleaseManifest(FirstPin, Digest(zip)), token);
        var first = Assert.Single((await catalog.RefreshAsync(source, token)).Releases);
        await root.Store.InstallAsync(first.Pin, RuntimeIdentifier, AdapterApi, first.ManifestBytes, zip, token);

        var candidate = ReleaseManifest(NextPin, LinuxHash);
        var mismatchedId = Encoding.UTF8.GetString(candidate).Replace("\"id\": \"uae\"", "\"id\": \"other\"", StringComparison.Ordinal);
        await PublishManifestAsync(git, Encoding.UTF8.GetBytes(mismatchedId), token);
        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.RefreshAsync(source, token));

        Directory.Move(git.Remote, git.Remote + ".offline");
        await Assert.ThrowsAsync<IOException>(() => catalog.RefreshAsync(source, token));
        var reopened = new EmulatorBucketCatalog(root.Directory);
        var cached = Assert.IsType<EmulatorBucketCatalog.Snapshot>(await reopened.ReadCachedAsync(source, token));
        Assert.Equal(first.Pin, Assert.Single(cached.Releases).Pin);
        Assert.NotNull(await reopened.ReadPinnedAsync(source, first.Pin, allowFetch: false, token));
        Assert.NotNull(root.Store.Resolve(first.Pin, RuntimeIdentifier, AdapterApi));
        var differentSource = source with { Repository = new Uri(git.Remote + ".different") };
        Assert.Null(await reopened.ReadCachedAsync(differentSource, token));
        Assert.Null(await reopened.ReadPinnedAsync(differentSource, first.Pin, allowFetch: false, token));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest; TEST-RETRO-009#ac-test-bundle-versions.</remarks>
    [Fact]
    public async Task Older_pins_survive_catalog_refresh_and_a_changed_artifact_cannot_reuse_a_version()
    {
        using var root = new BundleStoreFixture();
        using var freshCache = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var source = new Source("official", new Uri(git.Remote));
        var catalog = new EmulatorBucketCatalog(root.Directory);
        var priorRevision = await PublishManifestAsync(git, ReleaseManifest(FirstPin, LinuxHash), token);
        var prior = Assert.Single((await catalog.RefreshAsync(source, token)).Releases);
        await PublishManifestAsync(git, ReleaseManifest(NextPin, WindowsHash), token);
        var newer = await catalog.RefreshAsync(source, token);
        Assert.Equal(NextRelease, Assert.Single(newer.Releases).Pin.Version, StringComparer.Ordinal);
        var original = Assert.IsType<EmulatorBucketCatalog.Release>(await catalog.ReadPinnedAsync(source, prior.Pin, allowFetch: false, token));
        Assert.Equal(prior.ManifestBytes.ToArray(), original.ManifestBytes.ToArray());

        var otherHost = new EmulatorBucketCatalog(freshCache.Directory);
        var fetched = Assert.IsType<EmulatorBucketCatalog.Release>(await otherHost.ReadPinnedAsync(source, prior.Pin, allowFetch: true, token));
        Assert.Equal(priorRevision, fetched.Pin.ManifestRevision, StringComparer.Ordinal);

        var drift = await PublishManifestAsync(git, ReleaseManifest(FirstPin, WindowsHash), token);
        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.RefreshAsync(source, token));
        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ReadPinnedAsync(source, prior.Pin with { ManifestRevision = drift },
            allowFetch: false, token));
        var stillCurrent = Assert.IsType<EmulatorBucketCatalog.Snapshot>(await catalog.ReadCachedAsync(source, token));
        Assert.Equal(newer.Revision, stillCurrent.Revision, StringComparer.Ordinal);
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest.</remarks>
    [Fact]
    public async Task Concurrent_refreshes_publish_one_verified_snapshot()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var source = new Source("official", new Uri(git.Remote));
        var revision = await PublishManifestAsync(git, ReleaseManifest(FirstPin, LinuxHash), token);
        var first = new EmulatorBucketCatalog(root.Directory);
        var second = new EmulatorBucketCatalog(root.Directory);
        var results = await Task.WhenAll(first.RefreshAsync(source, token), second.RefreshAsync(source, token));
        Assert.All(results, snapshot => Assert.Equal(revision, snapshot.Revision, StringComparer.Ordinal));
        var cached = Assert.IsType<EmulatorBucketCatalog.Snapshot>(await second.ReadCachedAsync(source, token));
        Assert.Equal(revision, cached.Revision, StringComparer.Ordinal);
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task A_manifest_stored_as_a_symbolic_link_cannot_replace_the_verified_catalog()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var catalog = new EmulatorBucketCatalog(root.Directory);
        var source = new Source("official", new Uri(git.Remote));
        var revision = await PublishManifestAsync(git, ReleaseManifest(FirstPin, LinuxHash), token);
        await catalog.RefreshAsync(source, token);

        const string path = "bucket/uae.json";
        var blob = (await git.GitAsync(git.Primary, token, "rev-parse", "HEAD:" + path)).Trim();
        await git.GitAsync(git.Primary, token, "update-index", "--cacheinfo", "120000," + blob + "," + path);
        await git.GitAsync(git.Primary, token, "commit", "-m", "Replace the manifest with a link");
        await git.GitAsync(git.Primary, token, "push", "origin", "main");
        var rejected = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.RefreshAsync(source, token));
        Assert.Contains("regular JSON file", rejected.Message, StringComparison.Ordinal);
        var cached = Assert.IsType<EmulatorBucketCatalog.Snapshot>(await catalog.ReadCachedAsync(source, token));
        Assert.Equal(revision, cached.Revision, StringComparer.Ordinal);
    }

    private static async Task<string> PublishManifestAsync(GitWorktreeFixture git, byte[] bytes, CancellationToken token)
    {
        const string manifestPath = "bucket/uae.json";
        var directory = Directory.CreateDirectory(Path.Combine(git.Primary, "bucket"));
        await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "uae.json"), bytes, token).ConfigureAwait(false);
        await git.GitAsync(git.Primary, token, "add", "--", manifestPath).ConfigureAwait(false);
        await git.GitAsync(git.Primary, token, "commit", "-m", "Publish bundle manifest").ConfigureAwait(false);
        await git.GitAsync(git.Primary, token, "push", "origin", "main").ConfigureAwait(false);
        return (await git.GitAsync(git.Primary, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
    }
}
