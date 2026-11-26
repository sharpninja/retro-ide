using System.Diagnostics;
using System.Security.AccessControl;

namespace RetroIde.Application.Git;

/// <summary>Prepares a Git checkout in an operation-owned directory and installs it after authorization is rechecked.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Keep existing files intact and never expose a partial clone in the workspace.</remarks>
internal sealed class PreparedGitRepository : IDisposable
{
    private enum InstallKind { Reopen, NewDirectory, AddMetadata, ReplaceEmptyDirectory }

    private readonly string? _staging;
    private readonly InstallKind _kind;

    private PreparedGitRepository(GitWorktree repository, InstallKind kind, string? staging = null)
    {
        Repository = repository;
        _kind = kind;
        _staging = staging;
    }

    internal GitWorktree Repository { get; }

    private string Destination => Repository.WorktreePath;

    private string StagedWorktree => Path.Combine(_staging!, "checkout");

    internal static async Task<PreparedGitRepository> PrepareAsync(string destination, string? source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(destination))
            throw new Failure("git_invalid_destination", "Choose an absolute destination folder.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
            throw new Failure("git_invalid_destination", "Choose a destination with an existing parent folder.");

        var exists = IsPlainDirectory(path);
        var gitDirectory = Path.Combine(path, ".git");
        if (source is null && exists && EntryAttributes(gitDirectory) is { } metadata)
        {
            if (metadata.HasFlag(FileAttributes.ReparsePoint) ||
                await NativeGit.DiscoverAsync(path, "", token).ConfigureAwait(false) is not { } existing ||
                !PathEquals(existing.Repository.WorktreePath, path))
                throw new Failure("git_invalid_destination", "The destination has Git metadata that cannot be reopened.");
            return new PreparedGitRepository(existing.Repository, InstallKind.Reopen);
        }

        if (source is not null && exists)
            RequireEmpty(path);

        var kind = !exists ? InstallKind.NewDirectory : source is null ? InstallKind.AddMetadata : InstallKind.ReplaceEmptyDirectory;
        var staging = Path.Combine(kind == InstallKind.AddMetadata ? path : parent, $".retro-ide-git-{Guid.NewGuid():N}");
        var prepared = new PreparedGitRepository(new GitWorktree(path, gitDirectory, gitDirectory), kind, staging);
        try
        {
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(staging);
            else
                Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var worktree = prepared.StagedWorktree;
            var result = source is null
                ? await NativeGit.RunAsync(staging, ["init", "--", worktree], token).ConfigureAwait(false)
                : await NativeGit.RunAsync(parent, ["-c", "protocol.ext.allow=never", "clone", "--no-hardlinks", "--", source, worktree], token).ConfigureAwait(false);
            if (result is null || !Directory.Exists(Path.Combine(worktree, ".git")))
                throw source is null
                    ? new Failure("git_init_failed", "Git could not initialize the repository.")
                    : new Failure("git_clone_failed", "Git could not clone the repository. Verify the source and its access settings.");
            token.ThrowIfCancellationRequested();
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    internal void Install()
    {
        switch (_kind)
        {
            case InstallKind.Reopen:
                if (!IsPlainDirectory(Destination) || EntryAttributes(Path.Combine(Destination, ".git")) is not { } metadata ||
                    metadata.HasFlag(FileAttributes.ReparsePoint))
                    throw new Failure("git_destination_changed", "Git metadata changed while the repository was being reopened.");
                break;
            case InstallKind.NewDirectory:
                if (EntryAttributes(Destination) is not null)
                    throw new Failure("git_destination_changed", "Another operation created the destination.");
                Directory.Move(StagedWorktree, Destination);
                break;
            case InstallKind.AddMetadata:
                if (!IsPlainDirectory(Destination) || EntryAttributes(Repository.GitDirectory) is not null)
                    throw new Failure("git_destination_changed", "Another operation changed the destination or its Git metadata.");
                Directory.Move(Path.Combine(StagedWorktree, ".git"), Repository.GitDirectory);
                break;
            case InstallKind.ReplaceEmptyDirectory:
                ReplaceEmptyDirectory();
                break;
            default:
                throw new UnreachableException();
        }
    }

    private void ReplaceEmptyDirectory()
    {
        if (!IsPlainDirectory(Destination))
            throw new Failure("git_destination_changed", "The empty clone destination changed during preparation.");

        RequireEmpty(Destination);
        var backup = Path.Combine(Path.GetDirectoryName(Destination)!, $".retro-ide-git-{Guid.NewGuid():N}-empty");
        Directory.Move(Destination, backup);
        try
        {
            // A file created during the rename may now be in the backup. Never discard it.
            RequireEmpty(backup);
            PreservePermissions(backup, StagedWorktree);
            Directory.Move(StagedWorktree, Destination);
        }
        catch
        {
            try
            {
                if (EntryAttributes(Destination) is null)
                    Directory.Move(backup, Destination);
                else
                    Directory.Delete(backup, recursive: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new Failure("git_destination_changed", $"The destination changed. The previous folder is preserved at '{backup}'.");
            }
            throw;
        }
        RemoveDirectory(backup, recursive: false);
    }

    private static void PreservePermissions(string original, string staged)
    {
        if (OperatingSystem.IsWindows())
        {
            var permissions = new DirectoryInfo(original).GetAccessControl(AccessControlSections.Access);
            new DirectoryInfo(staged).SetAccessControl(permissions);
        }
        else
        {
            File.SetUnixFileMode(staged, File.GetUnixFileMode(original));
        }
    }

    private static void RequireEmpty(string path)
    {
        if (Directory.EnumerateFileSystemEntries(path).Any())
            throw new Failure("git_destination_not_empty", "Clone requires a new or empty destination folder.");
    }

    private static bool IsPlainDirectory(string path)
    {
        if (EntryAttributes(path) is not { } attributes)
            return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new Failure("git_invalid_destination", "Choose a directory instead of a file or symbolic link.");
        return true;
    }

    private static FileAttributes? EntryAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static bool PathEquals(string first, string second) =>
        (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Equals(first, second);

    private static void RemoveDirectory(string path, bool recursive)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            if (recursive && OperatingSystem.IsWindows())
            {
                // The staging clone owns its copied Git objects. Leave filesystem links alone.
                var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", enumeration))
                    if (file.IsReadOnly)
                        file.IsReadOnly = false;
            }
            Directory.Delete(path, recursive);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_staging is not null)
            RemoveDirectory(_staging, recursive: true);
    }

    internal sealed class Failure(string code, string message) : IOException(message)
    {
        internal string Code { get; } = code;
    }
}
