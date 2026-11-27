using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RetroIde.Application.Git;

namespace RetroIde.Application.Emulators;

/// <summary>Refreshes Git bucket manifests and retains verified revisions for offline resolution.</summary>
/// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest.</remarks>
internal sealed class EmulatorBucketCatalog(string root)
{
    private const string CurrentRef = "refs/retroide/catalog";
    private const string PendingRef = "refs/retroide/pending";
    private readonly string _root = Path.GetFullPath(root);

    internal async Task<Snapshot> RefreshAsync(Source source, CancellationToken cancellationToken)
    {
        var cache = CacheDirectory(source);
        Directory.CreateDirectory(cache);
        using var refresh = await BundleFileLock.OpenExclusiveAsync(Path.Combine(cache, "refresh.lock"), cancellationToken).ConfigureAwait(false);
        var repository = await EnsureRepositoryAsync(cache, source, cancellationToken).ConfigureAwait(false);
        try
        {
            var revision = await FetchAsync(repository, source, "HEAD", cancellationToken).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(repository, source.Id, revision, cancellationToken).ConfigureAwait(false);
            await VerifyAndRetainAsync(repository, snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await SetRefAsync(repository, CurrentRef, revision, CancellationToken.None).ConfigureAwait(false);
            return snapshot;
        }
        finally
        {
            await ClearPendingAsync(repository).ConfigureAwait(false);
        }
    }

    internal async Task<Snapshot?> ReadCachedAsync(Source source, CancellationToken cancellationToken)
    {
        var repository = Path.Combine(CacheDirectory(source), "repository");
        if (!Directory.Exists(repository))
            return null;
        var revision = await ReadCommitAsync(repository, CurrentRef, cancellationToken).ConfigureAwait(false);
        return revision is null ? null : await ReadSnapshotAsync(repository, source.Id, revision, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Release?> ReadPinnedAsync(Source source, EmulatorBundleStore.Pin pin, bool allowFetch,
        CancellationToken cancellationToken)
    {
        pin = pin.Normalized();
        var cache = CacheDirectory(source);
        if (!string.Equals(pin.Bucket, source.Id, StringComparison.Ordinal))
            throw new ArgumentException("The selected pin belongs to another Git bucket.", nameof(pin));
        var repository = Path.Combine(cache, "repository");
        if (!allowFetch && !Directory.Exists(repository))
            return null;

        Directory.CreateDirectory(cache);
        using var refresh = await BundleFileLock.OpenExclusiveAsync(Path.Combine(cache, "refresh.lock"), cancellationToken).ConfigureAwait(false);
        if (allowFetch)
            repository = await EnsureRepositoryAsync(cache, source, cancellationToken).ConfigureAwait(false);
        var revision = await ReadCommitAsync(repository, pin.ManifestRevision, cancellationToken).ConfigureAwait(false);
        var fetch = revision is null && allowFetch;
        try
        {
            if (fetch)
                revision = await FetchAsync(repository, source, pin.ManifestRevision, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(revision, pin.ManifestRevision, StringComparison.Ordinal))
                return null;

            var snapshot = await ReadSnapshotAsync(repository, source.Id, pin.ManifestRevision, cancellationToken).ConfigureAwait(false);
            var release = snapshot.Releases.SingleOrDefault(item => item.Pin == pin);
            if (release is not null)
                await VerifyAndRetainAsync(repository, snapshot, cancellationToken).ConfigureAwait(false);
            return release;
        }
        finally
        {
            if (fetch)
                await ClearPendingAsync(repository).ConfigureAwait(false);
        }
    }

    private string CacheDirectory(Source source)
    {
        var repository = source.Repository;
        if (!EmulatorBundleManifest.IsIdentifier(source.Id) || !repository.IsAbsoluteUri ||
            repository.Scheme is not ("https" or "http" or "ssh" or "file") || repository.Fragment.Length != 0 ||
            (repository.UserInfo.Length != 0 && (repository.Scheme is not "ssh" || repository.UserInfo.Contains(':'))))
            throw new ArgumentException("A Git bucket requires a portable ID and an absolute repository URL.", nameof(source));
        return Path.Combine(_root, "catalogs", source.Id, HashKey(repository.AbsoluteUri));
    }

    private static async Task<string> EnsureRepositoryAsync(string cache, Source source, CancellationToken token)
    {
        var repository = Path.Combine(cache, "repository");
        if (Directory.Exists(repository))
            return repository;

        var staging = Path.Combine(cache, Guid.NewGuid().ToString("N"));
        try
        {
            var result = await NativeGit.RunAsync(cache, TransportArguments(
                ["clone", "--bare", "--no-local", "--no-tags", "--template=", "--", source.Repository.AbsoluteUri, staging]), token)
                .ConfigureAwait(false);
            if (result is null)
                throw new IOException("The configured Git bucket could not be cloned.");
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, repository);
            return repository;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task<string> FetchAsync(string repository, Source source, string revision, CancellationToken token)
    {
        var arguments = TransportArguments(["fetch", "--force", "--no-tags", "--no-write-fetch-head", "--no-auto-gc",
            "--no-recurse-submodules", "--", source.Repository.AbsoluteUri, revision + ":" + PendingRef]);
        var output = await GitAsync(repository, arguments, token).ConfigureAwait(false);
        if (output is null)
            throw new IOException("The configured Git bucket could not be fetched.");
        return await ReadCommitAsync(repository, PendingRef, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The fetched Git bucket revision is not a commit.");
    }

    private static async Task<Snapshot> ReadSnapshotAsync(string repository, string bucket, string revision, CancellationToken token)
    {
        var tree = await GitAsync(repository, ["ls-tree", "-z", "-l", revision + ":bucket"], token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The Git bucket does not contain the expected bucket directory.");
        if (tree.Length != 0 && !tree.EndsWith('\0'))
            throw new InvalidDataException("The Git bucket directory listing is incomplete.");

        var releases = new List<Release>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            token.ThrowIfCancellationRequested();
            var tab = item.IndexOf('\t');
            if (tab < 0)
                throw new InvalidDataException("The Git bucket directory listing is invalid.");
            var filename = item[(tab + 1)..];
            if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = filename[..^5];
            var fields = item[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields is not [("100644" or "100755"), "blob", var objectId, var length] ||
                !GitHistoryOperations.IsObjectId(objectId) || !EmulatorBundleManifest.IsIdentifier(id) ||
                !filename.EndsWith(".json", StringComparison.Ordinal) ||
                !int.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
                size > EmulatorBundleManifest.MaximumBytes)
                throw new InvalidDataException("A Git bucket manifest must be a regular JSON file with a portable ID and bounded size.");

            var bytes = await ReadBlobAsync(repository, objectId, token).ConfigureAwait(false);
            if (bytes.Length != size)
                throw new InvalidDataException("The Git bucket manifest length differs from the tree entry.");
            var manifest = EmulatorBundleManifest.Parse(bytes);
            if (!string.Equals(id, manifest.Id, StringComparison.Ordinal))
                throw new InvalidDataException("The Git bucket manifest ID does not match its filename.");
            if (!ids.Add(id))
                throw new InvalidDataException("The Git bucket contains a duplicate manifest ID.");
            releases.Add(new Release(new EmulatorBundleStore.Pin(bucket, id, manifest.Version, revision), manifest, bytes));
        }
        return new Snapshot(revision, releases.AsReadOnly());
    }

    private static async Task VerifyAndRetainAsync(string repository, Snapshot snapshot, CancellationToken token)
    {
        var unseen = new List<string>();
        foreach (var release in snapshot.Releases)
        {
            var pin = release.Pin;
            var reference = "refs/retroide/releases/" + pin.Bundle + "/" + HashKey(pin.Version);
            var prior = await ReadCommitAsync(repository, reference, token).ConfigureAwait(false);
            if (prior is null)
            {
                unseen.Add(reference);
                continue;
            }
            if (string.Equals(prior, snapshot.Revision, StringComparison.Ordinal))
                continue;
            var bytes = await ReadBlobAsync(repository, prior + ":bucket/" + pin.Bundle + ".json", token).ConfigureAwait(false);
            if (!SameRelease(EmulatorBundleManifest.Parse(bytes), release.Manifest))
                throw new InvalidDataException($"The Git bucket changed the published release '{pin.Bundle}' version '{pin.Version}'.");
        }

        // Keep exact accepted commits available after a publisher rewrites its branch.
        await SetRefAsync(repository, "refs/retroide/snapshots/" + snapshot.Revision, snapshot.Revision, token).ConfigureAwait(false);
        foreach (var reference in unseen)
            await SetRefAsync(repository, reference, snapshot.Revision, token).ConfigureAwait(false);
    }

    private static bool SameRelease(EmulatorBundleManifest left, EmulatorBundleManifest right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
        string.Equals(left.AdapterId, right.AdapterId, StringComparison.Ordinal) &&
        left.AdapterApi.Count == right.AdapterApi.Count && left.AdapterApi.All(right.AdapterApi.Contains) &&
        left.DebuggerProfiles.Count == right.DebuggerProfiles.Count &&
        left.DebuggerProfiles.All(item => right.DebuggerProfiles.TryGetValue(item.Key, out var profile) && profile == item.Value) &&
        left.Targets.Count == right.Targets.Count &&
        left.Targets.All(item => right.Targets.TryGetValue(item.Key, out var target) && SameArtifact(item.Value, target));

    private static bool SameArtifact(EmulatorBundleArtifact left, EmulatorBundleArtifact right) =>
        string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal) &&
        left.EntryPoints.Count == right.EntryPoints.Count &&
        left.EntryPoints.All(item => right.EntryPoints.TryGetValue(item.Key, out var path) &&
            string.Equals(path, item.Value, StringComparison.Ordinal));

    private static async Task<string?> ReadCommitAsync(string repository, string reference, CancellationToken token)
    {
        var value = (await GitAsync(repository, ["rev-parse", "--verify", "--end-of-options", reference + "^{commit}"], token)
            .ConfigureAwait(false))?.Trim();
        return GitHistoryOperations.IsObjectId(value) ? value : null;
    }

    private static async Task<byte[]> ReadBlobAsync(string repository, string objectId, CancellationToken token) =>
        await NativeGit.ReadBytesAsync(repository, RepositoryArguments(repository, ["cat-file", "blob", objectId]), token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The Git bucket manifest blob is missing.");

    private static async Task SetRefAsync(string repository, string reference, string revision, CancellationToken token)
    {
        if (await GitAsync(repository, ["update-ref", "--no-deref", reference, revision], token).ConfigureAwait(false) is null)
            throw new IOException("The verified Git bucket revision could not be retained.");
    }

    private static Task<string?> ClearPendingAsync(string repository) =>
        GitAsync(repository, ["update-ref", "--no-deref", "-d", PendingRef], CancellationToken.None);

    private static Task<string?> GitAsync(string repository, IReadOnlyList<string> command, CancellationToken token) =>
        NativeGit.RunAsync(repository, RepositoryArguments(repository, command), token);

    private static List<string> RepositoryArguments(string repository, IReadOnlyList<string> command) =>
        ["--no-replace-objects", "--git-dir", repository, "-c", "core.hooksPath=" + Path.Combine(repository, "disabled-hooks"), .. command];

    private static List<string> TransportArguments(IReadOnlyList<string> command) =>
        ["-c", "protocol.allow=never", "-c", "protocol.https.allow=always", "-c", "protocol.http.allow=always",
         "-c", "protocol.ssh.allow=always", "-c", "protocol.file.allow=always", "-c", "protocol.ext.allow=never", .. command];

    private static string HashKey(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal sealed record Source(string Id, Uri Repository);
    internal sealed record Snapshot(string Revision, IReadOnlyList<Release> Releases);
    internal sealed record Release(EmulatorBundleStore.Pin Pin, EmulatorBundleManifest Manifest, ReadOnlyMemory<byte> ManifestBytes);
}
