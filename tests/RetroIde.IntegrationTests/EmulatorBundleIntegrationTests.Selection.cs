using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RetroIde.Application.Emulators;
using RetroIde.Testing;
using Xunit;
using Source = RetroIde.Application.Emulators.EmulatorBucketCatalog.Source;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bucket-platforms.</remarks>
    [Fact]
    public async Task The_installer_downloads_the_exact_pinned_artifact_and_reuses_it_offline()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var archive = LinuxReleaseArchive();
        var file = archive.ToArray();
        var requests = 0;
        await using var server = await StartArtifactServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.ContentLength = file.Length;
            await context.Response.Body.WriteAsync(file, context.RequestAborted).ConfigureAwait(false);
        }, token);
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var source = new Source("official", new Uri(git.Remote));
        var catalog = new EmulatorBucketCatalog(root.Directory);
        var bytes = WithArtifact(ReleaseManifest(FirstPin, Digest(archive)), server.Urls.Single() + "/bundle.zip");
        var revision = await PublishManifestAsync(git, bytes, token);
        var pin = FirstPin with { ManifestRevision = revision };
        using var http = new HttpClient();
        var installer = new EmulatorBundleInstaller(catalog, root.Store, http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(source, pin, "linux-arm64", AdapterApi, token));
        Assert.Equal(0, Volatile.Read(ref requests));
        var installed = await installer.InstallAsync(source, pin, RuntimeIdentifier, AdapterApi, token);
        Assert.Equal(1, Volatile.Read(ref requests));
        Assert.Equal(pin, installed.Receipt.Pin);
        Assert.Equal(Digest(archive), installed.Receipt.ArchiveSha256, StringComparer.Ordinal);

        await server.StopAsync(token);
        Directory.Move(git.Remote, git.Remote + ".offline");
        var reused = await installer.InstallAsync(source, pin, RuntimeIdentifier, AdapterApi, token);
        Assert.Equal(installed.Receipt, reused.Receipt);
        Assert.Equal(1, Volatile.Read(ref requests));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bucket-platforms.</remarks>
    [Fact]
    public async Task A_local_bucket_can_install_an_artifact_from_a_file_URI()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        using var archive = LinuxReleaseArchive();
        var path = Path.Combine(root.Directory, "verified bundle.zip");
        await File.WriteAllBytesAsync(path, archive.ToArray(), token);
        var revision = await PublishManifestAsync(git,
            WithArtifact(ReleaseManifest(FirstPin, Digest(archive)), new Uri(path).AbsoluteUri), token);
        var source = new Source("official", new Uri(git.Remote));
        using var http = new HttpClient();
        var installer = new EmulatorBundleInstaller(new EmulatorBucketCatalog(root.Directory), root.Store, http);
        var installed = await installer.InstallAsync(source, FirstPin with { ManifestRevision = revision }, RuntimeIdentifier, AdapterApi, token);
        Assert.Equal(Digest(archive), installed.Receipt.ArchiveSha256, StringComparer.Ordinal);
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Interrupting_a_streaming_download_leaves_the_selected_installation_usable()
    {
        using var root = new BundleStoreFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(root.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var old = LinuxReleaseArchive();
        await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, ReleaseManifest(FirstPin, Digest(old)), old, token);
        using var next = LinuxReleaseArchive("next-runtime.so");
        var file = next.ToArray();
        var partiallySent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartArtifactServerAsync(async context =>
        {
            context.Response.ContentLength = file.Length;
            await context.Response.Body.WriteAsync(file.AsMemory(0, 12), context.RequestAborted).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            partiallySent.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted).ConfigureAwait(false);
        }, token);
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var source = new Source("official", new Uri(git.Remote));
        var catalog = new EmulatorBucketCatalog(root.Directory);
        var revision = await PublishManifestAsync(git, WithArtifact(ReleaseManifest(NextPin, Digest(next)),
            server.Urls.Single() + "/bundle.zip"), token);
        var pin = NextPin with { ManifestRevision = revision };
        using var http = new HttpClient();
        var installer = new EmulatorBundleInstaller(catalog, root.Store, http);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var operation = installer.InstallAsync(source, pin, RuntimeIdentifier, AdapterApi, cancelled.Token);
        await partiallySent.Task.WaitAsync(token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Null(root.Store.Resolve(pin, RuntimeIdentifier, AdapterApi));
        Assert.NotNull(root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));
    }

    /// <remarks>FR-RETRO-009#ac-version-pins; TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution; TEST-RETRO-009#ac-test-bundle-versions.</remarks>
    [Fact]
    public async Task Project_selection_survives_restart_and_rolls_back_to_a_previous_verified_installation()
    {
        using var root = new BundleStoreFixture();
        var folder = Directory.CreateDirectory(Path.Combine(root.Directory, "project")).FullName;
        var settings = new EmulatorProjectSelectionStore(folder, root.Directory, root.Store);
        Assert.Null(settings.Read());
        Assert.False(Directory.Exists(Path.Combine(folder, ".retroide")));
        await InstallFixtureReleaseAsync(root, FirstPin);
        await settings.SelectAsync(FirstPin, RuntimeIdentifier, AdapterApi, root.CancellationToken);
        await InstallFixtureReleaseAsync(root, NextPin);
        Assert.Equal(FirstPin, settings.Read()?.Active);
        await settings.SelectAsync(NextPin, RuntimeIdentifier, AdapterApi, root.CancellationToken);

        var reopened = new EmulatorProjectSelectionStore(folder, root.Directory, root.Store);
        Assert.Equal(FirstPin, Assert.Single(reopened.Read()!.Previous));
        using var running = Assert.IsType<EmulatorBundleStore.BundleLease>(reopened.AcquireActive(RuntimeIdentifier, AdapterApi));
        Assert.Equal(NextPin, running.Installation.Receipt.Pin);
        var rolledBack = await reopened.RollbackAsync(FirstPin, RuntimeIdentifier, AdapterApi, root.CancellationToken);
        Assert.Equal(FirstPin, rolledBack.Active);
        Assert.Equal(NextPin, Assert.Single(rolledBack.Previous));
        Assert.Equal(EmulatorBundleStore.Removal.InUse, root.Store.Remove(NextPin, RuntimeIdentifier, root.CancellationToken));

        using var newSession = Assert.IsType<EmulatorBundleStore.BundleLease>(settings.AcquireActive(RuntimeIdentifier, AdapterApi));
        Assert.Equal(FirstPin, newSession.Installation.Receipt.Pin);
        var projectFile = Path.Combine(folder, ".retroide/emulator.json");
        var copied = Directory.CreateDirectory(Path.Combine(root.Directory, "other workstation", ".retroide"));
        File.Copy(projectFile, Path.Combine(copied.FullName, "emulator.json"));
        var emptyStore = new EmulatorBundleStore(Path.Combine(root.Directory, "other workstation bundles"));
        var portable = new EmulatorProjectSelectionStore(copied.Parent!.FullName, root.Directory, emptyStore);
        Assert.Equal(FirstPin, portable.Read()?.Active);
        Assert.Throws<InvalidOperationException>(() => portable.AcquireActive(RuntimeIdentifier, AdapterApi));
    }

    /// <remarks>FR-RETRO-009#ac-version-pins; TEST-RETRO-009#ac-test-bundle-versions.</remarks>
    [Fact]
    public async Task Concurrent_project_selections_preserve_both_pins_and_incompatible_rollbacks_keep_the_current_pin()
    {
        using var root = new BundleStoreFixture();
        var folder = Directory.CreateDirectory(Path.Combine(root.Directory, "project")).FullName;
        await InstallFixtureReleaseAsync(root, FirstPin);
        await InstallFixtureReleaseAsync(root, NextPin);
        var first = new EmulatorProjectSelectionStore(folder, root.Directory, root.Store);
        var second = new EmulatorProjectSelectionStore(folder, root.Directory, root.Store);
        await Task.WhenAll(Task.Run(() => first.SelectAsync(FirstPin, RuntimeIdentifier, AdapterApi, root.CancellationToken), root.CancellationToken),
            Task.Run(() => second.SelectAsync(NextPin, RuntimeIdentifier, AdapterApi, root.CancellationToken), root.CancellationToken));

        var latest = Assert.IsType<EmulatorProjectSelectionStore.Snapshot>(first.Read());
        var previous = Assert.Single(latest.Previous);
        Assert.Contains(latest.Active, new[] { FirstPin, NextPin });
        Assert.Contains(previous, new[] { FirstPin, NextPin });
        Assert.NotEqual(latest.Active, previous);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.RollbackAsync(previous, RuntimeIdentifier,
            new Version(5, 0), root.CancellationToken));
        Assert.Equal(latest.Active, second.Read()?.Active);
        Assert.Equal(previous, Assert.Single(second.Read()!.Previous));
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution.</remarks>
    [Fact]
    public async Task Malformed_project_pins_are_reported_and_the_file_remains_untouched()
    {
        using var root = new BundleStoreFixture();
        var folder = Directory.CreateDirectory(Path.Combine(root.Directory, "project")).FullName;
        var directory = Directory.CreateDirectory(Path.Combine(folder, ".retroide"));
        var path = Path.Combine(directory.FullName, "emulator.json");
        const string malformed = """{"schemaVersion":1,"active":{"bucket":"official"},"previous":[]}""";
        await File.WriteAllTextAsync(path, malformed, root.CancellationToken);
        await InstallFixtureReleaseAsync(root, FirstPin);
        var settings = new EmulatorProjectSelectionStore(folder, root.Directory, root.Store);
        Assert.Throws<InvalidDataException>(() => settings.Read());
        await Assert.ThrowsAsync<InvalidDataException>(() => settings.SelectAsync(FirstPin, RuntimeIdentifier, AdapterApi, root.CancellationToken));
        Assert.Equal(malformed, await File.ReadAllTextAsync(path, root.CancellationToken), StringComparer.Ordinal);
    }

    private static async Task InstallFixtureReleaseAsync(BundleStoreFixture root, EmulatorBundleStore.Pin pin)
    {
        using var archive = LinuxReleaseArchive();
        await root.Store.InstallAsync(pin, RuntimeIdentifier, AdapterApi, ReleaseManifest(pin, Digest(archive)), archive,
            root.CancellationToken).ConfigureAwait(false);
    }

    private static byte[] WithArtifact(byte[] manifest, string uri) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest)
        .Replace("https://example.invalid/uae-linux-x64.zip", uri, StringComparison.Ordinal));

    private static async Task<WebApplication> StartArtifactServerAsync(RequestDelegate send, CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet("/bundle.zip", send);
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            return app;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
