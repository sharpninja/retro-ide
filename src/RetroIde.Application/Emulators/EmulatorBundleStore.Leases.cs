namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorBundleStore
{
    internal BundleLease? Acquire(Pin pin, string runtimeIdentifier, Version adapterApi)
    {
        pin = Normalize(pin, runtimeIdentifier);
        var directory = InstallPath(pin, runtimeIdentifier);
        if (!Directory.Exists(directory))
            return null;

        var access = OpenShared(directory);
        try
        {
            if (Directory.Exists(directory))
                return new BundleLease(ReadInstalled(directory, pin, runtimeIdentifier, adapterApi), access);
        }
        catch
        {
            access.Dispose();
            throw;
        }

        access.Dispose();
        return null;
    }

    internal Removal Remove(Pin pin, string runtimeIdentifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = InstallPath(Normalize(pin, runtimeIdentifier), runtimeIdentifier);
        if (!Directory.Exists(directory))
            return Removal.Missing;

        var access = TryOpenExclusive(directory);
        if (access is null)
            return Removal.InUse;

        string retired;
        using (access)
        {
            if (!Directory.Exists(directory))
                return Removal.Missing;
            RequirePathType(directory, directoryExpected: true);
            var staging = Directory.CreateDirectory(Path.Combine(_root, "staging"));
            retired = Path.Combine(staging.FullName, Guid.NewGuid().ToString("N"));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(directory, retired);
        }

        Directory.Delete(retired, recursive: true);
        return Removal.Removed;
    }

    private static FileStream OpenShared(string directory) =>
        new(LeasePath(directory), FileMode.Open, FileAccess.Read, FileShare.Read);

    private static FileStream? TryOpenExclusive(string directory)
    {
        try
        {
            return new FileStream(LeasePath(directory), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error) when (BundleFileLock.IsContended(error))
        {
            return null;
        }
    }

    private static void EnsureLeaseFile(string staging, string directory)
    {
        var destination = LeasePath(directory);
        if (File.Exists(destination))
            return;

        // Publish a closed lock file. Concurrent installers never replace its inode.
        var pending = Path.Combine(staging, "lease");
        using (File.Open(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            File.Move(pending, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            File.Delete(pending);
        }
    }

    // Keep the lock outside the installation so removal can rename the complete version while holding it.
    private static string LeasePath(string directory) => directory + ".lease";

    internal enum Removal { Missing, InUse, Removed }

    internal sealed class BundleLease(InstalledBundle installation, FileStream access) : IDisposable
    {
        internal InstalledBundle Installation { get; } = installation;

        public void Dispose() => access.Dispose();
    }
}
