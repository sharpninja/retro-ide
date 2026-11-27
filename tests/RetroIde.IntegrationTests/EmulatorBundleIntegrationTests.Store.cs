using System.Text;
using RetroIde.Application.Emulators;
using RetroIde.Testing;
using Xunit;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    private const string RuntimeIdentifier = "linux-x64";
    private const string FirstRelease = "1.2.0-beta.1";
    private const string NextRelease = "1.2.0";
    private static readonly Version AdapterApi = new(1, 0);
    private static readonly Pin FirstPin = new("official", "uae", FirstRelease, new string('a', 40));
    private static readonly Pin NextPin = new("official", "uae", NextRelease, new string('b', 40));

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bucket-platforms.</remarks>
    [Fact]
    public async Task Atomic_install_saves_the_exact_revision_and_resolves_an_offline_installation()
    {
        using var root = new BundleStoreFixture();
        using var archive = LinuxReleaseArchive();
        var bytes = ReleaseManifest(FirstPin, Digest(archive));
        var installed = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, bytes, archive, root.CancellationToken);
        Assert.Equal(FirstPin, installed.Receipt.Pin);
        Assert.Equal(RuntimeIdentifier, installed.Receipt.RuntimeIdentifier, StringComparer.Ordinal);
        Assert.Equal(Digest(archive), installed.Receipt.ArchiveSha256, StringComparer.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(installed.Directory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(installed.Directory, "receipt.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));

        var offline = new EmulatorBundleStore(root.Directory);
        var restored = Assert.IsType<EmulatorBundleStore.InstalledBundle>(offline.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
        Assert.Equal(installed.Receipt, restored.Receipt);
        Assert.Equal("adapter/Uae.dll", await File.ReadAllTextAsync(restored.EntryPoint("binding"), root.CancellationToken), StringComparer.Ordinal);
        Assert.Null(offline.Resolve(FirstPin with { ManifestRevision = NextPin.ManifestRevision }, RuntimeIdentifier, AdapterApi));
        Assert.Throws<InvalidOperationException>(() => offline.Resolve(FirstPin, RuntimeIdentifier, new Version(2, 0)));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Failed_and_interrupted_updates_leave_the_installed_release_usable()
    {
        using var root = new BundleStoreFixture();
        using var archive = LinuxReleaseArchive();
        await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, ReleaseManifest(FirstPin, Digest(archive)), archive, root.CancellationToken);
        archive.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => root.Store.InstallAsync(NextPin, RuntimeIdentifier, AdapterApi,
            ReleaseManifest(NextPin, LinuxHash), archive, root.CancellationToken));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(root.CancellationToken);
        using var interrupted = new InterruptedArchive(archive.ToArray(), cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => root.Store.InstallAsync(NextPin, RuntimeIdentifier, AdapterApi,
            ReleaseManifest(NextPin, Digest(archive)), interrupted, cancellation.Token));
        Assert.Null(root.Store.Resolve(NextPin, RuntimeIdentifier, AdapterApi));
        Assert.NotNull(root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution; TEST-RETRO-009#ac-test-bundle-versions.</remarks>
    [Fact]
    public async Task Installing_a_new_version_and_selecting_a_prior_receipt_leave_each_pin_intact()
    {
        using var root = new BundleStoreFixture();
        using var previous = LinuxReleaseArchive();
        using var newer = LinuxReleaseArchive("new-runtime.so");
        var oldInstall = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi,
            ReleaseManifest(FirstPin, Digest(previous)), previous, root.CancellationToken);
        var newInstall = await root.Store.InstallAsync(NextPin, RuntimeIdentifier, AdapterApi,
            ReleaseManifest(NextPin, Digest(newer)), newer, root.CancellationToken);
        Assert.NotEqual(oldInstall.Directory, newInstall.Directory, StringComparer.Ordinal);
        var reopened = new EmulatorBundleStore(root.Directory);
        Assert.Equal(newInstall.Receipt, reopened.Resolve(NextPin, RuntimeIdentifier, AdapterApi)?.Receipt);
        Assert.Equal(oldInstall.Receipt, reopened.Resolve(FirstPin, RuntimeIdentifier, AdapterApi)?.Receipt);
        Assert.False(File.Exists(Path.Combine(oldInstall.Directory, "files/bin/new-runtime.so")));
        Assert.True(File.Exists(Path.Combine(newInstall.Directory, "files/bin/new-runtime.so")));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Concurrent_installs_reuse_one_receipt_and_never_replace_an_immutable_revision()
    {
        using var root = new BundleStoreFixture();
        using var first = LinuxReleaseArchive();
        using var second = new MemoryStream(first.ToArray());
        var manifest = ReleaseManifest(FirstPin, Digest(first));
        var otherStore = new EmulatorBundleStore(root.Directory);
        var results = await Task.WhenAll(
            root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, manifest, first, root.CancellationToken),
            otherStore.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, manifest, second, root.CancellationToken));
        Assert.Equal(results[0].Receipt, results[1].Receipt);
        Assert.Equal(results[0].Directory, results[1].Directory, StringComparer.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi,
            ReleaseManifest(FirstPin, LinuxHash), Stream.Null, root.CancellationToken));
        Assert.Equal(results[0].Receipt, root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi)?.Receipt);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution.</remarks>
    [Fact]
    public async Task A_receipt_rejects_changed_manifest_bytes_and_missing_payload_files()
    {
        using var root = new BundleStoreFixture();
        using var archive = LinuxReleaseArchive();
        var manifest = ReleaseManifest(FirstPin, Digest(archive));
        var installed = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, manifest, archive, root.CancellationToken);
        var manifestPath = Path.Combine(installed.Directory, "manifest.json");
        File.AppendAllText(manifestPath, " ");
        Assert.Throws<InvalidDataException>(() => root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
        File.WriteAllBytes(manifestPath, manifest);
        var binding = installed.EntryPoint("binding");
        File.Delete(binding);
        Assert.Throws<InvalidDataException>(() => root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution; TEST-RETRO-009#ac-test-bundle-versions.</remarks>
    [Fact]
    public async Task Independent_session_leases_prevent_removal_until_the_last_session_releases_the_version()
    {
        using var root = new BundleStoreFixture();
        using var archive = LinuxReleaseArchive();
        var bytes = ReleaseManifest(FirstPin, Digest(archive));
        var installed = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, bytes, archive, root.CancellationToken);

        using var firstSession = Assert.IsType<EmulatorBundleStore.BundleLease>(root.Store.Acquire(FirstPin, RuntimeIdentifier, AdapterApi));
        using var secondSession = Assert.IsType<EmulatorBundleStore.BundleLease>(root.Store.Acquire(FirstPin, RuntimeIdentifier, AdapterApi));
        Assert.Equal(installed.Receipt, firstSession.Installation.Receipt);
        Assert.Equal(installed.Receipt, secondSession.Installation.Receipt);
        Assert.Equal(EmulatorBundleStore.Removal.InUse, root.Store.Remove(FirstPin, RuntimeIdentifier, root.CancellationToken));
        firstSession.Dispose();
        Assert.Equal(EmulatorBundleStore.Removal.InUse, root.Store.Remove(FirstPin, RuntimeIdentifier, root.CancellationToken));
        Assert.True(File.Exists(secondSession.Installation.EntryPoint("emulator")));

        secondSession.Dispose();
        Assert.Equal(EmulatorBundleStore.Removal.Removed, root.Store.Remove(FirstPin, RuntimeIdentifier, root.CancellationToken));
        Assert.Equal(EmulatorBundleStore.Removal.Missing, root.Store.Remove(FirstPin, RuntimeIdentifier, root.CancellationToken));
        Assert.Null(root.Store.Acquire(FirstPin, RuntimeIdentifier, AdapterApi));
        Assert.False(Directory.Exists(installed.Directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));

        archive.Position = 0;
        var reinstalled = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, bytes, archive, root.CancellationToken);
        Assert.Equal(installed.Receipt, reinstalled.Receipt);
    }

    private static MemoryStream LinuxReleaseArchive(string runtime = "runtime.so") =>
        Archive(("adapter/Uae.dll", 0), ("bin/uae", 0x81ed), ("profiles/amiga-aga.json", 0), ("bin/" + runtime, 0));

    private static byte[] ReleaseManifest(Pin pin, string digest) => Encoding.UTF8.GetBytes(Manifest
        .Replace(FirstRelease, pin.Version, StringComparison.Ordinal)
        .Replace(LinuxHash, digest, StringComparison.Ordinal));

    private sealed class BundleStoreFixture : IDisposable
    {
        private readonly DirectoryInfo _root = System.IO.Directory.CreateTempSubdirectory("retroide-bundle-store-");

        internal string Directory => _root.FullName;
        internal EmulatorBundleStore Store => new(Directory);
        internal CancellationToken CancellationToken => TestContext.Current.CancellationToken;

        public void Dispose() => GitWorktreeFixture.DeleteOwnedDirectory(_root);
    }
}
