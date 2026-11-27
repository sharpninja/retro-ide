using System.Text.Json;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorBundleStore
{
    private const string ProfileReceiptFile = "debugger-profiles.json";
    private const int MaximumProfileReceiptBytes = 32 * 1024;

    private static string? StageProfileReceipt(string staging, EmulatorBundleManifest manifest, CancellationToken token)
    {
        if (manifest.DebuggerProfiles.Count == 0)
            return null;

        var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var files = Path.Combine(staging, "files");
        foreach (var (id, asset) in manifest.DebuggerProfiles)
        {
            token.ThrowIfCancellationRequested();
            var bytes = ReadProfileAsset(files, asset.Path);
            _ = DebuggerPresentationProfile.Read(bytes, id, asset.SchemaVersion);
            digests.Add(id, Digest(bytes));
        }

        var index = JsonSerializer.SerializeToUtf8Bytes(digests, StoreJson.Default.SortedDictionaryStringString);
        WriteDocument(staging, ProfileReceiptFile, index);
        return Digest(index);
    }

    private static byte[] ReadProfileAsset(string files, string path)
    {
        RequirePayloadFile(files, path);
        return ReadDocument(files, path, DebuggerPresentationProfile.MaximumBytes);
    }

    internal sealed partial record InstalledBundle
    {
        internal AdapterResult<DebuggerPresentationProfile> ReadDebuggerProfile(DebuggerProfileReference reference)
        {
            if (!Manifest.DebuggerProfiles.TryGetValue(reference.Id, out var asset) || Receipt.DebuggerProfilesSha256 is null)
                return ProfileUnavailable(AdapterErrorCodes.Unavailable, "The pinned bundle has no verified debugger profile with this ID.");
            if (asset.SchemaVersion != reference.SchemaVersion || reference.SchemaVersion != DebuggerPresentationProfile.CurrentVersion)
                return ProfileUnavailable(AdapterErrorCodes.Unsupported, "The pinned debugger profile uses an incompatible schema version.");

            var sidecar = ReadDocument(Directory, ProfileReceiptFile, MaximumProfileReceiptBytes);
            if (!string.Equals(Receipt.DebuggerProfilesSha256, Digest(sidecar), StringComparison.Ordinal))
                return ProfileUnavailable(AdapterErrorCodes.Incompatible, "The pinned bundle's debugger profile receipt has changed.");
            var digests = JsonSerializer.Deserialize(sidecar, StoreJson.Default.SortedDictionaryStringString);
            var bytes = ReadProfileAsset(Path.Combine(Directory, "files"), asset.Path);
            if (digests is null || !digests.TryGetValue(reference.Id, out var expected) ||
                !string.Equals(expected, Digest(bytes), StringComparison.Ordinal))
                return ProfileUnavailable(AdapterErrorCodes.Incompatible, "The pinned debugger profile differs from its verified package asset.");

            var profile = DebuggerPresentationProfile.Read(bytes, reference.Id, reference.SchemaVersion);
            return profile is null
                ? ProfileUnavailable(AdapterErrorCodes.Unsupported, "The pinned debugger profile uses an incompatible schema version.")
                : AdapterResult<DebuggerPresentationProfile>.Success(profile);
        }

        private static AdapterResult<DebuggerPresentationProfile> ProfileUnavailable(string code, string message) =>
            AdapterResult<DebuggerPresentationProfile>.Failure(new(code, message));
    }
}
