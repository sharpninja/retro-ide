using System.IO.Compression;
using System.Security.Cryptography;

namespace RetroIde.Application.Emulators;

/// <summary>Verifies and extracts a bundle into an unpublished installation directory.</summary>
/// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-integrity.</remarks>
internal static class EmulatorBundleArchive
{
    private const long MaximumArchiveBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumPaths = 65_536;
    private const int MaximumPathLength = 1_024;
    private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    internal static async Task<string> StageAsync(string stagingRoot, Stream source, EmulatorBundleArtifact artifact,
        IEnumerable<EmulatorDebuggerProfileAsset> debuggerProfiles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(staging);
        else
            Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var archivePath = Path.Combine(staging, "bundle.zip");
        try
        {
            var buffer = new byte[64 * 1024];
            var spool = OpenFile(archivePath, FileAccess.ReadWrite, buffer.Length);
            await using (spool.ConfigureAwait(false))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await CopyAsync(source, spool, buffer, MaximumArchiveBytes, hash, cancellationToken).ConfigureAwait(false);
                var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (!string.Equals(actual, artifact.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("The bundle archive SHA256 does not match the manifest.");

                spool.Position = 0;
                using var archive = new ZipArchive(spool, ZipArchiveMode.Read, leaveOpen: true);
                var paths = IndexPaths(archive, cancellationToken);
                foreach (var path in artifact.EntryPoints.Values)
                    RequireFile(paths, path, "entry point");
                foreach (var profile in debuggerProfiles)
                    RequireFile(paths, profile.Path, "debugger profile");

                var files = Directory.CreateDirectory(Path.Combine(staging, "files"));
                await ExtractAsync(files.FullName, paths.Values, buffer, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(archivePath);
            return staging;
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static Dictionary<string, ArchivePath> IndexPaths(ZipArchive archive, CancellationToken cancellationToken)
    {
        var paths = new Dictionary<string, ArchivePath>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = entry.FullName.EndsWith('/');
            var path = directory ? entry.FullName[..^1] : entry.FullName;
            if (path.Length > MaximumPathLength || !BundleArchivePath.IsPortable(path))
                throw new InvalidDataException($"Invalid bundle archive path '{path}'.");
            ValidateEntryType(entry, directory);

            if (entry.Length < 0 || entry.Length > MaximumExpandedBytes - expandedBytes)
                throw new InvalidDataException("The expanded bundle exceeds the permitted archive size.");
            expandedBytes += entry.Length;

            for (var separator = path.IndexOf('/'); separator >= 0; separator = path.IndexOf('/', separator + 1))
                AddPath(paths, path[..separator], directory: true, entry: null);
            AddPath(paths, path, directory, entry);
        }
        return paths;
    }

    private static void ValidateEntryType(ZipArchiveEntry entry, bool directory)
    {
        var attributes = entry.ExternalAttributes;
        var unixType = (attributes >> 16) & 0xf000;
        var expectedType = directory ? 0x4000 : 0x8000;
        if ((attributes & (int)FileAttributes.ReparsePoint) != 0 ||
            (unixType != 0 && unixType != expectedType) ||
            (directory && entry.Length != 0) ||
            (!directory && (attributes & (int)FileAttributes.Directory) != 0))
            throw new InvalidDataException($"Bundle archive entry '{entry.FullName}' must be a regular file or directory.");
    }

    private static void AddPath(Dictionary<string, ArchivePath> paths, string path, bool directory, ZipArchiveEntry? entry)
    {
        if (paths.TryGetValue(path, out var previous))
        {
            if (!string.Equals(path, previous.Path, StringComparison.Ordinal) || !directory || !previous.IsDirectory ||
                (previous.Entry is not null && entry is not null))
                throw new InvalidDataException($"Bundle archive path '{path}' aliases another entry.");
            if (entry is not null)
                paths[path] = previous with { Entry = entry };
            return;
        }

        if (paths.Count == MaximumPaths)
            throw new InvalidDataException("The bundle archive contains too many paths.");
        paths.Add(path, new ArchivePath(path, directory, entry));
    }

    private static void RequireFile(Dictionary<string, ArchivePath> paths, string path, string role)
    {
        if (!paths.TryGetValue(path, out var stored) || stored.IsDirectory || !string.Equals(path, stored.Path, StringComparison.Ordinal))
            throw new InvalidDataException($"The declared bundle {role} '{path}' is missing or is not a regular file.");
    }

    private static async Task ExtractAsync(string root, IEnumerable<ArchivePath> paths, byte[] buffer, CancellationToken cancellationToken)
    {
        foreach (var item in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(root, item.Path);
            if (item.Entry is not { } entry || item.IsDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            var output = OpenFile(destination, FileAccess.Write, buffer.Length);
            await using (output.ConfigureAwait(false))
            {
                var length = await CopyAsync(input, output, buffer, entry.Length, hash: null, cancellationToken).ConfigureAwait(false);
                if (length != entry.Length)
                    throw new InvalidDataException($"Bundle archive entry '{item.Path}' has an incorrect length.");
            }

            var execute = (UnixFileMode)(entry.ExternalAttributes >> 16) & ExecuteBits;
            if (!OperatingSystem.IsWindows() && execute != UnixFileMode.None)
                File.SetUnixFileMode(destination, File.GetUnixFileMode(destination) | execute);
        }
    }

    private static FileStream OpenFile(string path, FileAccess access, int bufferSize) =>
        new(path, FileMode.CreateNew, access, FileShare.None, bufferSize, FileOptions.Asynchronous);

    private static async Task<long> CopyAsync(Stream source, Stream target, byte[] buffer, long limit, IncrementalHash? hash,
        CancellationToken cancellationToken)
    {
        long written = 0;
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (count > limit - written)
                throw new InvalidDataException("The bundle archive exceeds its permitted size.");
            hash?.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            written += count;
        }
        return written;
    }

    private sealed record ArchivePath(string Path, bool IsDirectory, ZipArchiveEntry? Entry);
}
