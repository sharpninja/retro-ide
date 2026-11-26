using System.ComponentModel;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Handles literal file diffs and whole-file index changes for the captured checkout.</summary>
/// <remarks>2026-11-26 · FR-RETRO-013; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
internal static class GitFileOperations
{
    internal sealed record DiffInput(string WorktreePath, string Path, string Layer);
    internal sealed record FileInput(string WorktreePath, string Path);
    internal sealed record DiffResult(GitWorktree Repository, string Path, string Layer, string Patch);
    internal sealed record FileResult(GitWorktree Repository, string Path, bool Staged);

    internal static async Task<FeatureOutcome> DiffAsync(DiffInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (input.Layer is not ("staged" or "working"))
            return new("git_invalid_diff_layer", "Choose the staged or working diff layer.");

        return await WithFileAsync(input.WorktreePath, input.Path, view, async (selection, entry, scopedPath) =>
        {
            var repository = selection.Repository;
            var arguments = NativeGit.WorktreeArguments(repository);
            var staged = string.Equals(input.Layer, "staged", StringComparison.Ordinal);
            var untracked = !staged && string.Equals(entry?.Worktree, "?", StringComparison.Ordinal);
            arguments.AddRange(["diff", "--no-ext-diff", "--no-textconv", "--no-color"]);
            if (untracked)
                arguments.Add("--no-index");
            else if (staged)
                arguments.Add("--cached");

            arguments.Add("--");
            if (untracked)
                arguments.Add("/dev/null");
            arguments.AddRange(FilePaths(selection, entry, scopedPath, indexLayer: staged));
            var patch = await NativeGit.RunAsync(repository.WorktreePath, arguments, token,
                allowDifferences: untracked, strictEncoding: false).ConfigureAwait(false);
            if (patch is null)
                return new("git_diff_failed", "Git could not return a file diff.");

            return new FeatureOutcome("ok", "Git file diff read.")
            {
                Commit = current => HasSameSelection(current, selection)
                    ? FeatureOutcome.Success("Git file diff read.", new DiffResult(repository, scopedPath, input.Layer, patch))
                    : SelectionChanged(),
            };
        }, token).ConfigureAwait(false);
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

            return new FeatureOutcome("ok", "Git index change prepared.")
            {
                PreparedResource = prepared,
                Commit = current =>
                {
                    if (!HasSameSelection(current, selection))
                        return SelectionChanged();

                    string? failure;
                    try { failure = prepared.Publish(); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        return new("git_index_update_failed", "Git could not publish the index change.");
                    }
                    return failure switch
                    {
                        "git_index_busy" => new(failure, "Another process owns the index lock. Retry after it finishes."),
                        "git_index_changed" => new(failure, "The Git index changed. Refresh the diff and retry."),
                        _ => FeatureOutcome.Success(stage ? "Git file staged." : "Git file unstaged.",
                            new FileResult(selection.Repository, scopedPath, stage)),
                    };
                },
            };
        }, token);

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
