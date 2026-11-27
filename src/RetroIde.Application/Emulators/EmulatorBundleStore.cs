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
        if (Directory.Exists(destination))
            return Reuse(destination, receipt, adapterApi);

        var staging = await EmulatorBundleArchive.StageAsync(Path.Combine(_root, "staging"), source, artifact,
            manifest.DebuggerProfiles.Values, cancellationToken).ConfigureAwait(false);
        var published = false;
        try
        {
            WriteDocument(staging, ManifestFile, bytes);
            WriteDocument(staging, ReceiptFile, JsonSerializer.SerializeToUtf8Bytes(receipt, StoreJson.Default.Receipt));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Directory.Move(staging, destination);
                published = true;
            }
            catch (IOException) when (Directory.Exists(destination))
            {
                return Reuse(destination, receipt, adapterApi);
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
        pin = Normalize(pin, runtimeIdentifier);
        var directory = InstallPath(pin, runtimeIdentifier);
        return Directory.Exists(directory) ? ReadInstalled(directory, pin, runtimeIdentifier, adapterApi) : null;
    }

    private static Pin Normalize(Pin pin, string runtimeIdentifier)
    {
        if (!EmulatorBundleManifest.IsIdentifier(pin.Bucket) || !EmulatorBundleManifest.IsIdentifier(pin.Bundle) ||
            !EmulatorBundleManifest.IsReleaseVersion(pin.Version) || !GitHistoryOperations.IsObjectId(pin.ManifestRevision))
            throw new ArgumentException("A bundle pin must contain valid bucket, bundle, version, and Git revision values.", nameof(pin));
        if (!EmulatorBundleManifest.IsIdentifier(runtimeIdentifier))
            throw new ArgumentException("A host runtime identifier must be portable.", nameof(runtimeIdentifier));
        return pin with { ManifestRevision = pin.ManifestRevision.ToLowerInvariant() };
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

    private static InstalledBundle Reuse(string directory, Receipt expected, Version adapterApi)
    {
        var installed = ReadInstalled(directory, expected.Pin, expected.RuntimeIdentifier, adapterApi);
        if (installed.Receipt != expected)
            throw new InvalidDataException("The installed bundle revision does not match the requested manifest and archive.");
        return installed;
    }

    private static InstalledBundle ReadInstalled(string directory, Pin pin, string runtimeIdentifier, Version adapterApi)
    {
        try
        {
            RequirePathType(directory, directoryExpected: true);
            var receiptBytes = ReadDocument(directory, ReceiptFile, MaximumReceiptBytes);
            var receipt = JsonSerializer.Deserialize(receiptBytes, StoreJson.Default.Receipt);
            if (receipt is null || receipt.SchemaVersion != SchemaVersion || receipt.Pin != pin ||
                !string.Equals(receipt.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle receipt does not match the requested pin and host.");

            var bytes = ReadDocument(directory, ManifestFile, EmulatorBundleManifest.MaximumBytes);
            if (!string.Equals(receipt.ManifestSha256, Digest(bytes), StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle manifest differs from its receipt.");
            var manifest = ReadManifest(pin, bytes);
            var artifact = manifest.TargetFor(runtimeIdentifier, adapterApi);
            if (!string.Equals(receipt.ArchiveSha256, artifact.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("The installed bundle archive digest differs from its manifest.");

            var files = Path.Combine(directory, "files");
            RequirePathType(files, directoryExpected: true);
            foreach (var path in artifact.EntryPoints.Values)
                RequirePayloadFile(files, path);
            foreach (var profile in manifest.DebuggerProfiles.Values)
                RequirePayloadFile(files, profile.Path);
            return new InstalledBundle(receipt, manifest, ForwardSlashes(directory));
        }
        catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("The installed bundle receipt or payload is missing or invalid.", error);
        }
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

    internal sealed record Pin(string Bucket, string Bundle, string Version, string ManifestRevision);

    internal sealed record Receipt(int SchemaVersion, Pin Pin, string RuntimeIdentifier, string ArchiveSha256, string ManifestSha256);

    internal sealed record InstalledBundle(Receipt Receipt, EmulatorBundleManifest Manifest, string Directory)
    {
        internal string EntryPoint(string role) => Directory + "/files/" + Manifest.Targets[Receipt.RuntimeIdentifier].EntryPoints[role];
    }

    [JsonSerializable(typeof(Receipt))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    private sealed partial class StoreJson : JsonSerializerContext;
}
