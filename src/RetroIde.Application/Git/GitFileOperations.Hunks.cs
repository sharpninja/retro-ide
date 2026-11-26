
namespace RetroIde.Application.Git;

internal static partial class GitFileOperations
{
    internal sealed record HunksInput(string WorktreePath, string Path, string Preimage, int[] HunkIds);
    internal sealed record HunksResult(GitWorktree Repository, string Path, bool Staged, int[] HunkIds);

    internal static Task<FeatureOutcome> UpdateHunksAsync(HunksInput input, IWorkbenchRegistration view, bool stage, CancellationToken token)
    {
        if (input.HunkIds is not { Length: > 0 } || input.HunkIds.Any(id => id < 1) ||
            input.HunkIds.Distinct().Count() != input.HunkIds.Length)
            return Task.FromResult(new FeatureOutcome("git_invalid_hunks", "Choose one or more distinct hunk IDs from the current diff."));

        var layer = stage ? "working" : "staged";
        return WithFileAsync(input.WorktreePath, input.Path, view, async (selection, entry, path) =>
        {
            var file = Path.GetFullPath(path, selection.Repository.WorktreePath);
            var fileBefore = stage ? FileStamp.Read(file) : null;
            FeatureOutcome? prepareError = null;
            var prepared = await Task.Run(() => PreparedGitIndex.PrepareAsync(selection.Repository, async indexPath =>
            {
                var patch = await ReadPatchAsync(selection, entry, path, layer, token, indexPath).ConfigureAwait(false);
                var review = patch is null ? null : IdentifyHunks(patch, selection.Repository, entry, path, layer);
                if (review is null || !string.Equals(review.Preimage, input.Preimage, StringComparison.Ordinal))
                {
                    prepareError = ChangedDiff();
                    return false;
                }
                if (input.HunkIds.Any(id => id > review.Hunks.Length))
                {
                    prepareError = new("git_invalid_hunks", "At least one hunk does not exist in the current diff.");
                    return false;
                }

                var patchPath = indexPath + ".patch";
                await File.WriteAllBytesAsync(patchPath, review.Select(input.HunkIds), token).ConfigureAwait(false);
                var arguments = NativeGit.WorktreeArguments(selection.Repository);
                arguments.AddRange(["apply", "--cached", "--whitespace=nowarn"]);
                if (!stage)
                    arguments.Add("--reverse");
                arguments.AddRange(["--", patchPath]);
                return await NativeGit.RunAsync(selection.Repository.WorktreePath, arguments, token, indexPath).ConfigureAwait(false) is not null;
            }, token), token).ConfigureAwait(false);
            if (prepared is null)
                return prepareError ?? new("git_hunk_apply_failed", "Git could not apply the selected hunks to the index.");

            // The alternate index is frozen. Verify that the live diff still agrees before handing it off for publication.
            try
            {
                var patch = await ReadPatchAsync(selection, entry, path, layer, token).ConfigureAwait(false);
                var current = patch is null ? null : IdentifyHunks(patch, selection.Repository, entry, path, layer);
                if (!string.Equals(current?.Preimage, input.Preimage, StringComparison.Ordinal) ||
                    (stage && FileStamp.Read(file) != fileBefore))
                {
                    prepared.Dispose();
                    return ChangedDiff();
                }

                return CommitIndex(prepared, selection, FeatureOutcome.Success(stage ? "Git hunks staged." : "Git hunks unstaged.",
                    new HunksResult(selection.Repository, path, stage, input.HunkIds)),
                    stage ? () => FileStamp.Read(file) == fileBefore : null);
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }, token);
    }

    private static GitHunkPatch? IdentifyHunks(byte[] patch, GitWorktree repository, GitStatusEntry? entry, string path, string layer) =>
        entry is { IsUnmerged: false } && (string.Equals(layer, "staged", StringComparison.Ordinal) ? entry.Index : entry.Worktree) is "M"
            ? GitHunkPatch.Parse(patch, repository, path, layer)
            : null;

    private static FeatureOutcome ChangedDiff() => new("git_diff_changed", "The Git diff changed. Refresh it and select the hunks again.");

    private sealed record FileStamp(long Length, DateTime LastWrite, FileAttributes Attributes)
    {
        internal static FileStamp? Read(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? new(file.Length, file.LastWriteTimeUtc, file.Attributes) : null;
        }
    }
}
