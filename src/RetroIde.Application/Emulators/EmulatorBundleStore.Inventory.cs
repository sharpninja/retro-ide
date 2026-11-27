using System.Text.Json;
using RetroIde.Application.Git;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorBundleStore
{
    internal InstalledBundle[] Inventory(string runtimeIdentifier, Action<Exception> report, CancellationToken token)
    {
        if (!EmulatorBundleManifest.IsIdentifier(runtimeIdentifier))
            throw new ArgumentException("A host runtime identifier must be portable.", nameof(runtimeIdentifier));
        var releases = new List<InstalledBundle>();
        var directories = Folders(Path.Combine(_root, "installed")).SelectMany(Folders)
            .SelectMany(bundle => Folders(Path.Combine(bundle, runtimeIdentifier))).SelectMany(Folders);
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var access = OpenShared(directory);
                if (!Directory.Exists(directory))
                    continue;
                var receipt = ReadReceipt(directory);
                var pin = receipt.Pin.Normalized();
                if (!NativeGit.SamePath(directory, InstallPath(pin, runtimeIdentifier)))
                    throw new InvalidDataException("The bundle receipt does not match the installed directory.");
                releases.Add(ReadInstalled(directory, pin, runtimeIdentifier, adapterApi: null, receipt));
            }
            catch (IOException error) when (!Directory.Exists(directory) || BundleFileLock.IsContended(error)) { }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
            {
                report(error);
            }
        }
        return releases.ToArray();
    }

    private static IEnumerable<string> Folders(string path) => Directory.Exists(path)
        ? Directory.EnumerateDirectories(path, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint })
        : [];
}
