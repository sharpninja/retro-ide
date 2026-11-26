using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Runs native Git commit operations against the selected worktree's reviewed HEAD and index.</summary>
/// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
internal static class GitCommitOperations
{
    internal sealed record StateInput(string WorktreePath);
    internal sealed record CommitInput(string WorktreePath, string Preimage, string Message);
    internal sealed record CommitState(GitWorktree Repository, string Branch, string? Head, bool HasStagedChanges, string Preimage, string? Message);
    internal sealed record CommitResult(GitWorktree Repository, string? PreviousHead, string? Head, bool Amended, bool IndexPublished);

    internal static Task<FeatureOutcome> ReadAsync(StateInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSnapshotAsync(input.WorktreePath, view, (selection, snapshot) => new FeatureOutcome("ok", "Git commit state read.")
        {
            Commit = current => SameSelection(current, selection)
                ? FeatureOutcome.Success("Git commit state read.", snapshot.State)
                : SelectionChanged(),
        }, token, readMessage: true);

    internal static Task<FeatureOutcome> CommitAsync(CommitInput input, IWorkbenchRegistration view, bool amend, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Contains('\0'))
            return Task.FromResult(new FeatureOutcome("git_commit_message_required", "Enter a valid commit message."));

        return WithSnapshotAsync(input.WorktreePath, view, (selection, snapshot) =>
        {
            if (!string.Equals(input.Preimage, snapshot.State.Preimage, StringComparison.Ordinal))
                return Changed();
            if (amend && snapshot.State.Head is null)
                return new("git_no_commit", "This branch has no commit to amend.");
            if (!amend && !snapshot.State.HasStagedChanges)
                return new("git_no_staged_changes", "Stage changes before creating a commit.");

            return new FeatureOutcome("ok", "Git commit prepared.")
            {
                Commit = current => SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Git commit authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => Task.Run(
                            () => ExecuteAsync(selection, snapshot, input.Message, amend, cancellation), cancellation),
                    }
                    : SelectionChanged(),
            };
        }, token);
    }

    private static async Task<FeatureOutcome> WithSnapshotAsync(string path, IWorkbenchRegistration view,
        Func<GitSelection, Snapshot, FeatureOutcome> handle, CancellationToken token, bool readMessage = false)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var repository = selection.Repository;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(path, repository.WorktreePath, comparison))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!string.Equals(selection.ScopeRoot, repository.WorktreePath, comparison))
            return new("git_commit_scope_required", "Open the worktree root before committing its index.");

        try
        {
            var snapshot = await Task.Run(() => CaptureAsync(selection, token, readMessage), token).ConfigureAwait(false);
            return snapshot is null ? Changed() : handle(selection, snapshot);
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException)
        {
            return new("git_commit_state_unavailable", "Git could not read the selected worktree's commit state.");
        }
    }

    private static async Task<Snapshot?> CaptureAsync(GitSelection selection, CancellationToken token, bool readMessage = false)
    {
        var repository = selection.Repository;
        var path = Path.Combine(repository.GitDirectory, "index");
        var index = GitIndexLock.Read(path);
        var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false);
        if (status is null || !GitIndexLock.ContentsEqual(index, GitIndexLock.Read(path)))
            return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{repository.WorktreePath}\0{repository.GitDirectory}\0{status.Branch}\0{status.Head}\0{(index is null ? 0 : 1)}\0"));
        if (index is not null)
            hash.AppendData(index);
        string? message = null;
        if (readMessage && status.Head is { } head)
        {
            var query = NativeGit.WorktreeArguments(repository);
            query.AddRange(["show", "--no-notes", "--no-patch", "--format=%B", head, "--"]);
            message = (await NativeGit.RunAsync(repository.WorktreePath, query, token).ConfigureAwait(false))?.TrimEnd('\r', '\n');
        }
        var state = new CommitState(repository, status.Branch, status.Head,
            status.Entries.Any(entry => !entry.IsUnmerged && entry.Index is not ("." or "?" or "!")),
            Convert.ToHexString(hash.GetHashAndReset()), message);
        return new(state, index);
    }

    private static async Task<FeatureOutcome> ExecuteAsync(GitSelection selection, Snapshot expected, string message, bool amend, CancellationToken token)
    {
        var repository = selection.Repository;
        var index = Path.Combine(repository.GitDirectory, "index");
        var temporary = Path.Combine(repository.GitDirectory, $"retro-ide-commit-{Guid.NewGuid():N}");
        var messagePath = temporary + ".message";
        var committed = false;
        string? head = null;
        try
        {
            using var held = GitIndexLock.TryAcquire(index);
            if (held is null)
                return new("git_index_busy", "Another process owns the index lock. Retry after it finishes.");

            var current = await CaptureAsync(selection, token).ConfigureAwait(false);
            if (!string.Equals(current?.State.Preimage, expected.State.Preimage, StringComparison.Ordinal))
                return Changed();

            if (expected.Index is not null)
                await File.WriteAllBytesAsync(temporary, expected.Index, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(messagePath, message, token).ConfigureAwait(false);
            var args = NativeGit.WorktreeArguments(repository);
            args.AddRange(["commit", "--no-edit", $"--file={messagePath}"]);
            if (amend)
                args.Add("--amend");

            // Once native Git starts publishing the commit, wait for its exit so cancel does not
            // report a failure after Git has moved HEAD or interrupt the post-commit hooks.
            token.ThrowIfCancellationRequested();
            var result = await NativeGit.RunCaptureAsync(repository.WorktreePath, args, CancellationToken.None,
                indexPath: temporary, captureError: true).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return CommitFailure(result);

            committed = true;
            var query = NativeGit.WorktreeArguments(repository);
            query.AddRange(["rev-parse", "--verify", "HEAD"]);
            head = (await NativeGit.RunAsync(repository.WorktreePath, query, CancellationToken.None).ConfigureAwait(false))?.Trim();
            if (GitIndexLock.Read(temporary) is not { } updated)
                return Committed(indexPublished: false);
            if (!GitIndexLock.ContentsEqual(updated, expected.Index))
                held.Publish(updated, expected.Index is not null);
            return Committed(indexPublished: true);
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return committed ? Committed(indexPublished: false) : new("git_commit_failed", "Git could not complete the requested commit.");
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(temporary + ".lock");
            File.Delete(messagePath);
        }

        FeatureOutcome Committed(bool indexPublished) => FeatureOutcome.Success(
            indexPublished ? amend ? "Git commit amended." : "Git commit created." : "Git updated HEAD, but could not publish the prepared index. Refresh Git status.",
            new CommitResult(repository, expected.State.Head, head, amend, indexPublished));
    }

    private static FeatureOutcome CommitFailure(NativeGit.CommandResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.Error) ? Encoding.UTF8.GetString(result.Output) : result.Error;
        var diagnostic = string.Concat(error.Take(600).Where(character => !char.IsControl(character) || character is '\n' or '\t')).Trim();
        return new("git_commit_failed", diagnostic.Length > 0 ? $"Git declined the commit. {diagnostic}" : "Git declined the commit.");
    }

    private static bool SameSelection(IWorkbenchRegistration view, GitSelection selected) =>
        view is IGitRegistration git && ReferenceEquals(git.SelectedGit, selected);

    private static FeatureOutcome Changed() => new("git_commit_state_changed", "HEAD or the Git index changed. Review the current staged state and try again.");
    private static FeatureOutcome SelectionChanged() => new("git_selection_changed", "The selected Git worktree changed.");
    private sealed record Snapshot(CommitState State, byte[]? Index);
}
