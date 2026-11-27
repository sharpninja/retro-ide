using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RetroIde.Application.Git;

namespace RetroIde.Application.Emulators;

/// <summary>Publishes verified bundles and resolves exact project pins from the local store.</summary>
/// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution.</remarks>
internal sealed partial class EmulatorBundleStore(string root)
{
    private const int SchemaVersion = 1;
    private const int MaximumReceiptBytes = 4096;
    private const string ManifestFile = "manifest.json";
    private const string ReceiptFile = "receipt.json";
    private readonly string _root = Path.GetFullPath(root);

    internal async Task<InstalledBundle> InstallAsync(Pin pin, string runtimeIdentifier, Version adapterApi,
        ReadOnlyMemory<byte> manifestBytes, Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pin = Normalize(pin, runtimeIdentifier);
        if (manifestBytes.Length > EmulatorBundleManifest.MaximumBytes)
            throw new InvalidDataException("The bundle manifest exceeds its permitted size.");

        var bytes = manifestBytes.ToArray();
        var manifest = ReadManifest(pin, bytes);
        var artifact = manifest.TargetFor(runtimeIdentifier, adapterApi);
        var receipt = new Receipt(SchemaVersion, pin, runtimeIdentifier, artifact.Sha256, Digest(bytes));
        var destination = InstallPath(pin, runtimeIdentifier);
        using (var existing = Acquire(pin, runtimeIdentifier, adapterApi))
        {
            if (existing is not null)
                return Reuse(existing.Installation, receipt);
        }

        var staging = await EmulatorBundleArchive.StageAsync(Path.Combine(_root, "staging"), source, artifact,
            manifest.DebuggerProfiles.Values, cancellationToken).ConfigureAwait(false);
        var published = false;
        try
        {
            receipt = receipt with { DebuggerProfilesSha256 = StageProfileReceipt(staging, manifest, cancellationToken) };
            WriteDocument(staging, ManifestFile, bytes);
            WriteDocument(staging, ReceiptFile, JsonSerializer.SerializeToUtf8Bytes(receipt, StoreJson.Default.Receipt));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureLeaseFile(staging, destination);
            using var access = OpenShared(destination);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Directory.Move(staging, destination);
                published = true;
            }
            catch (IOException) when (Directory.Exists(destination))
            {
                return Reuse(ReadInstalled(destination, pin, runtimeIdentifier, adapterApi), receipt);
            }

            return new InstalledBundle(receipt, manifest, ForwardSlashes(destination));
        }
        finally
        {
            if (!published)
                Directory.Delete(staging, recursive: true);
        }
    }

    internal InstalledBundle? Resolve(Pin pin, string runtimeIdentifier, Version adapterApi)
    {
        using var access = Acquire(pin, runtimeIdentifier, adapterApi);
        return access?.Installation;
    }

    private static Pin Normalize(Pin pin, string runtimeIdentifier)
    {
        if (!EmulatorBundleManifest.IsIdentifier(runtimeIdentifier))
            throw new ArgumentException("A host runtime identifier must be portable.", nameof(runtimeIdentifier));
        return pin.Normalized();
    }

    private string InstallPath(Pin pin, string runtimeIdentifier)
    {
        // SemVer identifiers are case sensitive even when the store filesystem is not.
        var versionKey = Digest(Encoding.UTF8.GetBytes(pin.Version));
        return Path.Combine(_root, "installed", pin.Bucket, pin.Bundle, runtimeIdentifier, versionKey, pin.ManifestRevision);
    }

    private static EmulatorBundleManifest ReadManifest(Pin pin, byte[] raw)
    {
        var manifest = EmulatorBundleManifest.Parse(raw);
        if (!string.Equals(manifest.Id, pin.Bundle, StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, pin.Version, StringComparison.Ordinal))
            throw new InvalidDataException("The bundle manifest ID or version does not match the selected pin.");
        return manifest;
    }

    private static InstalledBundle Reuse(InstalledBundle installed, Receipt expected)
    {
        if (installed.Receipt with { DebuggerProfilesSha256 = null } != expected with { DebuggerProfilesSha256 = null } ||
            (installed.Receipt.DebuggerProfilesSha256 is { } actual && expected.DebuggerProfilesSha256 is { } staged &&
                !string.Equals(actual, staged, StringComparison.Ordinal)))
            throw new InvalidDataException("The installed bundle revision does not match the requested manifest and archive.");
        return installed;
    }

    private static InstalledBundle ReadInstalled(string directory, Pin pin, string runtimeIdentifier, Version? adapterApi, Receipt? receipt = null)
    {
        try
        {
            RequirePathType(directory, directoryExpected: true);
            receipt ??= ReadReceipt(directory);
            if (receipt.Pin != pin ||
                !string.Equals(receipt.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle receipt does not match the requested pin and host.");

            var bytes = ReadDocument(directory, ManifestFile, EmulatorBundleManifest.MaximumBytes);
            if (!string.Equals(receipt.ManifestSha256, Digest(bytes), StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle manifest differs from its receipt.");
            var manifest = ReadManifest(pin, bytes);
            if (adapterApi is not null)
                _ = manifest.TargetFor(runtimeIdentifier, adapterApi);
            if (!manifest.Targets.TryGetValue(runtimeIdentifier, out var artifact))
                throw new InvalidDataException("The installed bundle no longer declares its host target.");
            if (!string.Equals(receipt.ArchiveSha256, artifact.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle archive digest differs from its manifest.");

            var files = Path.Combine(directory, "files");
            RequirePathType(files, directoryExpected: true);
            foreach (var path in artifact.EntryPoints.Values)
                RequirePayloadFile(files, path);
            return new InstalledBundle(receipt, manifest, ForwardSlashes(directory));
        }
        catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("The installed bundle receipt or payload is missing or invalid.", error);
        }
    }

    private static Receipt ReadReceipt(string directory)
    {
        var bytes = ReadDocument(directory, ReceiptFile, MaximumReceiptBytes);
        return JsonSerializer.Deserialize(bytes, StoreJson.Default.Receipt) is { SchemaVersion: SchemaVersion, Pin: not null } receipt
            ? receipt
            : throw new InvalidDataException("The installed bundle receipt is invalid.");
    }

    private static void RequirePayloadFile(string root, string path)
    {
        for (var separator = path.IndexOf('/'); separator >= 0; separator = path.IndexOf('/', separator + 1))
            RequirePathType(Path.Combine(root, path[..separator]), directoryExpected: true);
        RequirePathType(Path.Combine(root, path), directoryExpected: false);
    }

    private static void RequirePathType(string path, bool directoryExpected)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory) != directoryExpected)
            throw new InvalidDataException("An installed bundle path has an unexpected file type.");
    }

    private static byte[] ReadDocument(string folder, string name, int maximumBytes)
    {
        var path = Path.Combine(folder, name);
        RequirePathType(path, directoryExpected: false);
        using var stream = File.OpenRead(path);
        var length = stream.Length;
        if (length > maximumBytes)
            throw new InvalidDataException($"The installed bundle {name} exceeds its storage limit.");
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteDocument(string folder, string name, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ForwardSlashes(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    internal sealed record Pin(string Bucket, string Bundle, string Version, string ManifestRevision)
    {
        internal Pin Normalized()
        {
            if (!EmulatorBundleManifest.IsIdentifier(Bucket) || !EmulatorBundleManifest.IsIdentifier(Bundle) ||
                !EmulatorBundleManifest.IsReleaseVersion(Version) || !GitHistoryOperations.IsObjectId(ManifestRevision))
                throw new ArgumentException("A bundle pin must contain valid bucket, bundle, version, and Git revision values.");
            return this with { ManifestRevision = ManifestRevision.ToLowerInvariant() };
        }
    }

    internal sealed record Receipt(int SchemaVersion, Pin Pin, string RuntimeIdentifier, string ArchiveSha256, string ManifestSha256,
        string? DebuggerProfilesSha256 = null);

    internal sealed partial record InstalledBundle(Receipt Receipt, EmulatorBundleManifest Manifest, string Directory)
    {
        internal string EntryPoint(string role) => Directory + "/files/" + Manifest.Targets[Receipt.RuntimeIdentifier].EntryPoints[role];
    }

    [JsonSerializable(typeof(Receipt))]
    [JsonSerializable(typeof(SortedDictionary<string, string>))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    private sealed partial class StoreJson : JsonSerializerContext;
}
