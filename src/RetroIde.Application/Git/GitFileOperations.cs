using System.ComponentModel;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Handles literal file diffs and index changes for the captured checkout.</summary>
/// <remarks>2026-11-26 · FR-RETRO-013; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
internal static partial class GitFileOperations
{
    internal sealed record DiffInput(string WorktreePath, string Path, string Layer);
    internal sealed record FileInput(string WorktreePath, string Path);
    internal sealed record CloseDiffInput(Guid ViewKey);
    internal sealed record DiffResult(GitWorktree Repository, string Path, string Layer, string Patch, string? Preimage, GitHunkPatch.Hunk[] Hunks);
    internal sealed record DiffTab(Guid ViewKey, Guid WorkspaceId, GitWorktree Repository, string Path, string Layer,
        string? Preimage, GitHunkPatch.Hunk[] Hunks);
    internal sealed record FileResult(GitWorktree Repository, string Path, bool Staged);

    internal static async Task<FeatureOutcome> DiffAsync(DiffInput input, IWorkbenchRegistration view, bool openTab, CancellationToken token)
    {
        if (input.Layer is not ("staged" or "working"))
            return new("git_invalid_diff_layer", "Choose the staged or working diff layer.");

        return await WithFileAsync(input.WorktreePath, input.Path, view, async (selection, entry, scopedPath) =>
        {
            var repository = selection.Repository;
            var patch = await ReadPatchAsync(selection, entry, scopedPath, input.Layer, token).ConfigureAwait(false);
            if (patch is null)
                return new("git_diff_failed", "Git could not return a file diff.");

            var review = IdentifyHunks(patch, repository, entry, scopedPath, input.Layer);
            var diff = new DiffResult(repository, scopedPath, input.Layer, Encoding.UTF8.GetString(patch), review?.Preimage, review?.Hunks ?? []);
            return new FeatureOutcome("ok", "Git file diff read.")
            {
                Commit = current =>
                {
                    if (current is not IGitRegistration git || !HasSameSelection(current, selection))
                        return SelectionChanged();
                    if (!openTab)
                        return FeatureOutcome.Success("Git file diff read.", diff);
                    return git.ShowDiff(diff) is { } tab
                        ? FeatureOutcome.Success("Git diff tab opened.", tab)
                        : new("view_closed", "The workbench cannot open a Git diff tab.");
                },
            };
        }, token).ConfigureAwait(false);
    }

    private static Task<byte[]?> ReadPatchAsync(GitSelection selection, GitStatusEntry? entry, string path, string layer,
        CancellationToken token, string? indexPath = null)
    {
        var repository = selection.Repository;
        var arguments = NativeGit.WorktreeArguments(repository);
        var staged = string.Equals(layer, "staged", StringComparison.Ordinal);
        var untracked = !staged && string.Equals(entry?.Worktree, "?", StringComparison.Ordinal);
        arguments.AddRange(["-c", "diff.suppressBlankEmpty=false", "diff", "--patch", "--no-ext-diff", "--no-textconv",
            "--no-color", "--full-index", "--unified=3", "--inter-hunk-context=0", "--src-prefix=a/", "--dst-prefix=b/", "--no-relative",
            "--output-indicator-new=+", "--output-indicator-old=-", "--output-indicator-context= "]);
        if (untracked)
            arguments.Add("--no-index");
        else if (staged)
            arguments.Add("--cached");
        arguments.Add("--");
        if (untracked)
            arguments.Add("/dev/null");
        arguments.AddRange(FilePaths(selection, entry, path, indexLayer: staged));
        return NativeGit.ReadBytesAsync(repository.WorktreePath, arguments, token, indexPath, allowDifferences: untracked);
    }

    internal static Task<FeatureOutcome> UpdateIndexAsync(FileInput input, IWorkbenchRegistration view, bool stage, CancellationToken token) =>
        WithFileAsync(input.WorktreePath, input.Path, view, async (selection, entry, scopedPath) =>
        {
            var paths = FilePaths(selection, entry, scopedPath, indexLayer: !stage);
            if (stage && paths.Any(path => IsDirectory(selection.Repository.WorktreePath, path)))
                return new("git_scope_invalid", "Choose a file rather than a directory.");
            var prepared = await Task.Run(() => PreparedGitIndex.CreateAsync(selection.Repository, paths, stage, token), token).ConfigureAwait(false);
            if (prepared is null)
                return new("git_index_update_failed", "Git could not prepare the index change.");

            return CommitIndex(prepared, selection, FeatureOutcome.Success(stage ? "Git file staged." : "Git file unstaged.",
                new FileResult(selection.Repository, scopedPath, stage)));
        }, token);

    private static FeatureOutcome CommitIndex(PreparedGitIndex prepared, GitSelection selection, FeatureOutcome success, Func<bool>? verifyWorktree = null) =>
        new("ok", "Git index change prepared.")
        {
            PreparedResource = prepared,
            Commit = current =>
            {
                if (!HasSameSelection(current, selection))
                    return SelectionChanged();

                string? failure;
                try { failure = prepared.Publish(verifyWorktree); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return new("git_index_update_failed", "Git could not publish the index change.");
                }
                return failure switch
                {
                    "git_index_busy" => new(failure, "Another process owns the index lock. Retry after it finishes."),
                    "git_index_changed" => new(failure, "The Git index changed. Refresh the diff and retry."),
                    "git_diff_changed" => ChangedDiff(),
                    _ => success,
                };
            },
        };

    private static async Task<FeatureOutcome> WithFileAsync(string worktreePath, string path, IWorkbenchRegistration view,
        Func<GitSelection, GitStatusEntry?, string, Task<FeatureOutcome>> operation, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var repo = selection.Repository;
        if (!string.Equals(repo.WorktreePath, worktreePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");

        try
        {
            if (!IsScopedFile(selection, path))
                return new("git_scope_invalid", "Choose a literal file path inside the selected worktree scope.");

            var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false);
            if (status is null)
                return new("git_repository_unavailable", "The path has no available Git worktree.");
            var entry = Array.Find(status.Entries, item => string.Equals(item.Path, path, StringComparison.Ordinal));
            if (string.Equals(entry?.Worktree, "!", StringComparison.Ordinal) ||
                (entry is null && !status.TrackedPaths.Contains(path, StringComparer.Ordinal)))
                return new("git_file_unavailable", "Choose a tracked or untracked file from the selected Git worktree.");
            return await operation(selection, entry, path).ConfigureAwait(false);
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return new("git_scope_invalid", "Choose a literal file path inside the selected worktree scope.");
        }
        catch (ScopeException)
        {
            return new("git_scope_invalid", "The rename crosses the selected workspace boundary.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidOperationException)
        {
            return new("git_file_unavailable", "Git could not read the selected file or index.");
        }
    }

    private static string[] FilePaths(GitSelection selection, GitStatusEntry? entry, string path, bool indexLayer)
    {
        if (entry?.OriginalPath is not { } original || (indexLayer ? entry.Index : entry.Worktree) is not ("R" or "C"))
            return [path];

        if (!IsScopedFile(selection, original))
            throw new ScopeException();
        return [original, path];
    }

    private static bool IsScopedFile(GitSelection selection, string path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || (OperatingSystem.IsWindows() && path.Contains('\\')))
            return false;
        if (path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." ||
            string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)))
            return false;

        return NativeGit.ContainsPath(selection.ScopeRoot, Path.GetFullPath(path, selection.Repository.WorktreePath));
    }

    private static bool IsDirectory(string worktree, string path)
    {
        var directory = new DirectoryInfo(Path.Combine(worktree, path));
        return directory.Exists && directory.LinkTarget is null;
    }

    private static bool HasSameSelection(IWorkbenchRegistration view, GitSelection selection) =>
        view is IGitRegistration git && ReferenceEquals(git.SelectedGit, selection);

    private static FeatureOutcome SelectionChanged() => new("git_selection_changed", "The selected Git worktree changed. Read the current selection and retry.");

    private sealed class ScopeException : Exception;
}
