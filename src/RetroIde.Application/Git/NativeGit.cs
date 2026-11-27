using System.Diagnostics;
using System.Text;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Uses native Git to discover repository identity and read the selected checkout's status.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-native-host, ac-git-machine-state. Use literal argument arrays and preserve the user's native Git configuration.</remarks>
internal static class NativeGit
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string[] WorkspaceOverrides =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_PREFIX", "GIT_INTERNAL_SUPER_PREFIX",
        "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_QUARANTINE_PATH", "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM", "GIT_IMPLICIT_WORK_TREE", "GIT_GRAFT_FILE",
        "GIT_SHALLOW_FILE", "GIT_LITERAL_PATHSPECS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS",
    ];

    /// <summary>Discovers the worktree owning an existing directory or file inside an open workspace root.</summary>
    /// <param name="root">The registered workspace root.</param>
    /// <param name="relative">Its resolved relative address.</param>
    /// <param name="token">Cancellation for the filesystem and native subprocess operations.</param>
    /// <returns>The discovered worktree with reusable status scope, or null for a path without a worktree.</returns>
    internal static async Task<GitSelection?> DiscoverAsync(string root, string relative, CancellationToken token)
    {
        var directory = await Task.Run(() =>
        {
            if (relative.Length != 0 && WorkspaceExplorer.TraversesLink(root, relative))
                return null;
            var path = Path.Combine(root, relative);
            return Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path) : null;
        }, token).ConfigureAwait(false);
        if (directory is null)
            return null;

        // Ask for each path separately because a native path may contain newlines.
        var top = await ResolveAsync(directory, "--show-toplevel", token).ConfigureAwait(false);
        if (top is null || !ContainsPath(top, directory))
            return null;
        var own = await ResolveAsync(directory, "--absolute-git-dir", token).ConfigureAwait(false);
        var common = await ResolveAsync(directory, "--git-common-dir", token).ConfigureAwait(false);
        if (own is null || common is null)
            return null;
        var scope = ContainsPath(root, top) ? top : root;
        return new GitSelection(new GitWorktree(top, own, common), scope);
    }

    /// <summary>Reads a captured checkout's status with optional index writes and external fsmonitor disabled.</summary>
    /// <param name="selection">The selected and authorized worktree.</param>
    /// <param name="token">Cancellation for the Git read.</param>
    /// <returns>The scoped status, or null if Git could no longer read the selected checkout.</returns>
    internal static async Task<GitStatus?> ReadStatusAsync(GitSelection selection, CancellationToken token)
    {
        var repo = selection.Repository;
        // Recheck the administrative directories in case the selected working directory was replaced.
        var found = await DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
        if (found is null || !IsSameWorktree(found.Repository, repo))
            return null;

        var tracked = await ReadAsync(["ls-files", "--cached", "--deduplicate", "--full-name", "-z"]).ConfigureAwait(false);
        if (tracked is null)
            return null;
        if (tracked.Length > 0 && !tracked.EndsWith('\0'))
            throw new InvalidDataException("Git index output is missing its final path terminator.");
        string[] statusCommand = ["status", "--porcelain=v2", "-z", "--branch", "--renames", "--untracked-files=all", "--ignored=matching"];
        var wholeWorktree = PathComparer.Equals(repo.WorktreePath, selection.ScopeRoot);
        var operation = wholeWorktree ? GitOperationRecovery.ReadKind(repo) : null;
        var output = await ReadAsync(statusCommand).ConfigureAwait(false);
        if (wholeWorktree && !string.Equals(operation, GitOperationRecovery.ReadKind(repo), StringComparison.Ordinal))
        {
            // A native command can publish its operation marker while the first status read is in progress.
            operation = GitOperationRecovery.ReadKind(repo);
            output = await ReadAsync(statusCommand).ConfigureAwait(false);
        }
        return output is null ? null : GitStatus.Parse(repo, output) with
        {
            TrackedPaths = tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries),
            OperationKind = operation,
        };

        Task<string?> ReadAsync(string[] command)
        {
            var arguments = WorktreeArguments(repo);
            arguments.AddRange(command);
            if (!PathComparer.Equals(repo.WorktreePath, selection.ScopeRoot))
            {
                arguments.Add("--");
                arguments.Add(Path.GetRelativePath(repo.WorktreePath, selection.ScopeRoot));
            }
            return RunAsync(repo.WorktreePath, arguments, token);
        }
    }

    internal static List<string> WorktreeArguments(GitWorktree repo) =>
        ["--no-optional-locks", "--literal-pathspecs", "--git-dir", repo.GitDirectory,
            "--work-tree", repo.WorktreePath, "-c", "core.fsmonitor=false"];

    internal static bool IsRepositoryOverride(string name) =>
        WorkspaceOverrides.Contains(name, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static bool SamePath(string? left, string? right) =>
        OperatingSystem.IsWindows()
            ? PathComparer.Equals(left?.Replace('\\', '/'), right?.Replace('\\', '/'))
            : PathComparer.Equals(left, right);

    internal static void DeleteOwnedCopy(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // A private clone owns its Git objects. Skip filesystem links when changing attributes.
            var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", enumeration))
                if (file.IsReadOnly)
                    file.IsReadOnly = false;
        }
        Directory.Delete(path, recursive: true);
    }

    /// <summary>Compares a worktree and both administrative directories using native path rules.</summary>
    internal static bool IsSameWorktree(GitWorktree left, GitWorktree right) =>
        SamePath(left.WorktreePath, right.WorktreePath) &&
        SamePath(left.GitDirectory, right.GitDirectory) &&
        SamePath(left.CommonDirectory, right.CommonDirectory);

    /// <summary>Checks path containment using native separators and volume rules.</summary>
    internal static bool ContainsPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && !string.Equals(relative, "..", StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>Reads an absolute Git path while retaining whitespace belonging to the path itself.</summary>
    private static async Task<string?> ResolveAsync(string directory, string option, CancellationToken token)
    {
        var output = await RunAsync(directory, ["rev-parse", "--path-format=absolute", option], token).ConfigureAwait(false);
        if (output is null || !output.EndsWith('\n'))
            return null;
        var path = output[..^1];
        if (OperatingSystem.IsWindows() && path.EndsWith('\r'))
            path = path[..^1];
        return Path.IsPathFullyQualified(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;
    }

    /// <summary>Runs Git asynchronously and decodes output without replacing invalid path bytes.</summary>
    internal static async Task<string?> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token,
        string? indexPath = null)
    {
        var output = await ReadBytesAsync(directory, arguments, token, indexPath).ConfigureAwait(false);
        return output is null ? null : new UTF8Encoding(false, true).GetString(output);
    }

    /// <summary>Preserves the original patch bytes emitted by Git.</summary>
    internal static async Task<byte[]?> ReadBytesAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token,
        string? indexPath = null, bool allowDifferences = false)
    {
        var result = await RunCaptureAsync(directory, arguments, token, indexPath).ConfigureAwait(false);
        return result.ExitCode == 0 || (allowDifferences && result.ExitCode == 1) ? result.Output : null;
    }

    internal sealed record CommandResult(int ExitCode, byte[] Output, string? Error);

    internal static async Task<CommandResult> RunCaptureAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token,
        string? indexPath = null, bool captureError = false, bool reuseCommitMessage = false)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var name in WorkspaceOverrides)
            start.Environment.Remove(name);
        if (indexPath is not null)
            start.Environment["GIT_INDEX_FILE"] = indexPath;
        if (reuseCommitMessage)
            start.Environment["GIT_EDITOR"] = "true";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        // Captured commands have no input protocol. Git LFS can wait on inherited stdin.
        process.StandardInput.Close();
        using var buffer = new MemoryStream();
        using var errorBuffer = captureError ? new MemoryStream() : null;
        var output = process.StandardOutput.BaseStream.CopyToAsync(buffer, token);
        var error = process.StandardError.BaseStream.CopyToAsync(errorBuffer ?? (Stream)Stream.Null, token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return new(process.ExitCode, buffer.ToArray(), errorBuffer is null ? null : Encoding.UTF8.GetString(errorBuffer.ToArray()));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(output, error).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
