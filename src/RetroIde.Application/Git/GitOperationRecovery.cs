using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.Git;

/// <summary>Reviews and recovers a paused native operation in the selected checkout.</summary>
/// <remarks>FR-RETRO-013; TEST-RETRO-013#ac-test-git-recovery.</remarks>
internal static class GitOperationRecovery
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly string[] ControlFiles =
    [
        "HEAD", "index", "ORIG_HEAD", "MERGE_HEAD", "MERGE_MODE", "MERGE_MSG", "MERGE_AUTOSTASH", "CHERRY_PICK_HEAD",
        "REVERT_HEAD", "REBASE_HEAD", "sequencer/head", "sequencer/todo", "sequencer/opts", "sequencer/abort-safety",
        "rebase-merge/head-name", "rebase-merge/orig-head", "rebase-merge/onto", "rebase-merge/git-rebase-todo",
        "rebase-merge/done", "rebase-merge/stopped-sha", "rebase-merge/message", "rebase-merge/autostash", "rebase-merge/update-refs",
        "rebase-merge/message-squash", "rebase-merge/message-fixup",
        "rebase-apply/head-name", "rebase-apply/orig-head", "rebase-apply/onto", "rebase-apply/next",
        "rebase-apply/last", "rebase-apply/original-commit", "rebase-apply/final-commit", "rebase-apply/autostash",
    ];

    internal sealed record ReadInput(string WorktreePath);
    internal sealed record RecoverInput(string WorktreePath, string Revision);
    internal enum Action { Continue, Skip, Abort }
    internal sealed record State(GitWorktree Repository, string Kind, string? Head, string? Revision, string[] Conflicts,
        bool CanContinue, bool CanAbort, string? PendingCommit = null)
    {
        public bool CanSkip => CanAbort && SupportsSkip(Kind);
    }

    internal static string? ReadKind(GitWorktree repo)
    {
        var directory = repo.GitDirectory;
        if (Directory.Exists(Path.Combine(directory, "rebase-merge")) ||
            File.Exists(Path.Combine(directory, "rebase-apply", "rebasing")))
            return "rebase";
        if (Directory.Exists(Path.Combine(directory, "rebase-apply")))
            return "am";
        if (File.Exists(Path.Combine(directory, "MERGE_HEAD")))
            return "merge";
        if (File.Exists(Path.Combine(directory, "CHERRY_PICK_HEAD")))
            return "cherry-pick";
        if (File.Exists(Path.Combine(directory, "REVERT_HEAD")))
            return "revert";
        var sequence = Path.Combine(directory, "sequencer");
        if (!Directory.Exists(sequence))
            return null;
        var action = ReadFile(Path.Combine(sequence, "todo"))?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.StartsWith('#'))?.Split(' ', 2)[0];
        return action switch { "pick" => "cherry-pick", "revert" => "revert", _ => "sequencer" };
    }

    internal static bool IsSupported(string? kind) => kind is "merge" or "rebase" or "cherry-pick" or "revert";
    internal static bool SupportsSkip(string? kind) => kind is "rebase" or "cherry-pick" or "revert";

    internal static Task<FeatureOutcome> ReadAsync(ReadInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSelectionAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var state = await CaptureAsync(selection, token).ConfigureAwait(false);
            return new FeatureOutcome("ok", "Git operation state read.")
            {
                Commit = current => SameSelection(current, selection)
                    ? FeatureOutcome.Success("Git operation state read.", state)
                    : ChangedSelection(),
            };
        }, token);

    internal static Task<FeatureOutcome> RecoverAsync(RecoverInput input, IWorkbenchRegistration view, Action action, CancellationToken token) =>
        WithSelectionAsync(input.WorktreePath, view, async (git, selection) =>
        {
            var state = await CaptureAsync(selection, token).ConfigureAwait(false);
            if (Validate(state, input.Revision, action) is { } problem)
                return problem;
            return new FeatureOutcome("ok", "Git operation recovery prepared.")
            {
                Commit = current => SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Git operation recovery authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => TryAsync(() => ApplyAsync(git, selection, input.Revision, action, cancellation)),
                    }
                    : ChangedSelection(),
            };
        }, token);

    private static async Task<FeatureOutcome> ApplyAsync(IGitRegistration git, GitSelection selection, string revision, Action action, CancellationToken token)
    {
        using var held = await GitConfigurationGate.EnterAsync(selection.Repository, token).ConfigureAwait(false);
        // Resuming a sequencer may also apply later commits. Hold all open buffers in this worktree until Git returns.
        var lease = await git.BeginIntegrationAsync(selection, paths: null, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return new(lease.ErrorCode ?? "git_operation_editors_busy", "The selection changed or an editor in this worktree has unsaved changes.");

        var invoked = false;
        try
        {
            var before = await CaptureAsync(selection, token).ConfigureAwait(false);
            if (Validate(before, revision, action) is { } problem)
                return problem;
            var repo = selection.Repository;
            if (action is Action.Abort or Action.Skip &&
                await WouldOverwriteUntrackedAsync(selection, before, action, token).ConfigureAwait(false))
                return new("git_operation_local_changes", "Move untracked or ignored files that this recovery could overwrite.");

            var arguments = NativeGit.WorktreeArguments(repo);
            var nativeOption = action switch { Action.Abort => "--abort", Action.Skip => "--skip", _ => "--continue" };
            arguments.AddRange(["-c", "submodule.recurse=false", before.Kind, nativeOption]);
            token.ThrowIfCancellationRequested();
            invoked = true;
            var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, CancellationToken.None,
                reuseCommitMessage: action is not Action.Abort).ConfigureAwait(false);
            var after = await CaptureAsync(selection, CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode == 0)
                return FeatureOutcome.Success(action switch
                {
                    Action.Abort => "Git operation aborted.",
                    Action.Skip => "Git skipped the current step.",
                    _ => "Git operation continued.",
                }, after);
            var paused = after.Conflicts.Length > 0 && action is not Action.Abort;
            return new FeatureOutcome(paused ? "git_operation_paused" : "git_operation_failed",
                paused ? "Git paused with unresolved conflicts." :
                    "Git could not recover the operation. Check worktree status and the native Git configuration.",
                JsonSerializer.SerializeToElement(after, WireJson.Options));
        }
        finally { await guard.CompleteAsync(invoked).ConfigureAwait(false); }
    }

    private static async Task<bool> WouldOverwriteUntrackedAsync(GitSelection selection, State state, Action action, CancellationToken token)
    {
        var repo = selection.Repository;
        var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git status is unavailable.");
        var local = status.Entries.Where(entry => entry.Worktree is "?" or "!").Select(entry => entry.Path.TrimEnd('/')).ToArray();
        if (local.Length == 0)
            return false;

        var original = action is Action.Skip ? status.Head : state.Kind switch
        {
            "rebase" => RelativeFile(repo, "rebase-merge/orig-head") ?? RelativeFile(repo, "rebase-apply/orig-head"),
            "merge" => RelativeFile(repo, "ORIG_HEAD"),
            _ => RelativeFile(repo, "sequencer/head") ?? status.Head,
        };
        if (!GitHistoryOperations.IsObjectId(original?.Trim()))
            throw new InvalidDataException("Git did not record a valid restore point.");
        var arguments = NativeGit.WorktreeArguments(repo);
        arguments.AddRange(["ls-tree", "-r", "--full-tree", "--name-only", "-z", original!.Trim()]);
        var tree = await NativeGit.RunAsync(repo.WorktreePath, arguments, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git could not inspect the restore point.");
        if (Collides(tree.Split('\0', StringSplitOptions.RemoveEmptyEntries), local))
            return true;
        return action is Action.Skip && await WouldReplayOverLocalFilesAsync(repo, state, local, token).ConfigureAwait(false);
    }

    private static async Task<bool> WouldReplayOverLocalFilesAsync(GitWorktree repo, State state, string[] local, CancellationToken token)
    {
        var path = state.Kind is "rebase" ? "rebase-merge/git-rebase-todo" : "sequencer/todo";
        var sequence = RelativeFile(repo, path);
        if (sequence is null)
            return false;

        var commits = new List<string>();
        foreach (var line in sequence.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || fields[0] is not ("pick" or "p" or "revert" or "reword" or "r" or "edit" or "e" or
                "squash" or "s" or "fixup" or "f" or "merge" or "m"))
                continue;
            var commit = fields[1] is "-c" or "-C" && fields.Length > 2 ? fields[2] : fields[1];
            if (commit.Length is >= 4 and <= 64 && commit.All(char.IsAsciiHexDigit))
                commits.Add(commit);
        }
        // A cherry-pick or revert todo starts with the paused step. Rebase keeps it in its done file.
        if (state.Kind is not "rebase" && commits.Count > 0 &&
            state.PendingCommit?.StartsWith(commits[0], StringComparison.OrdinalIgnoreCase) == true)
            commits.RemoveAt(0);
        foreach (var batch in commits.Chunk(64))
        {
            var arguments = NativeGit.WorktreeArguments(repo);
            arguments.AddRange(["log", "--no-walk=unsorted", "--root", "--diff-merges=first-parent", "--no-renames",
                "--no-ext-diff", "--no-textconv", "--name-only", "--format=", "-z", .. batch, "--"]);
            var paths = await NativeGit.RunAsync(repo.WorktreePath, arguments, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Git could not inspect the remaining sequence.");
            if (Collides(paths.Split('\0', StringSplitOptions.RemoveEmptyEntries), local))
                return true;
        }
        return false;
    }

    internal static bool Collides(string[] paths, string[] local) => paths.Any(path => local.Any(entry =>
        string.Equals(path, entry, PathComparison) || path.StartsWith(entry + "/", PathComparison) ||
        entry.StartsWith(path + "/", PathComparison)));

    private static FeatureOutcome? Validate(State state, string revision, Action action)
    {
        if (!state.CanAbort)
            return new("git_operation_unavailable", "No supported Git operation is paused in the selected worktree.");
        if (!string.Equals(state.Revision, revision, StringComparison.Ordinal))
            return new("git_operation_changed", "Git operation state changed. Review it again before recovery.");
        if (action is Action.Skip && !state.CanSkip)
            return new("git_operation_skip_unsupported", "Git does not support skipping a step in this operation.");
        return action is Action.Continue && !state.CanContinue
            ? new("git_operation_conflicts", "Resolve and stage all conflicts before continuing.") : null;
    }

    internal static async Task<State> CaptureAsync(GitSelection selection, CancellationToken token)
    {
        var repo = selection.Repository;
        var kind = ReadKind(repo);
        var supported = IsSupported(kind);
        var controls = supported ? CaptureControls(repo, token) : null;
        var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git status is unavailable.");
        var conflicts = status.Entries.Where(entry => entry.IsUnmerged).Select(entry => entry.Path).Order(StringComparer.Ordinal).ToArray();
        if (!supported)
            return new(repo, kind ?? "none", status.Head, null, conflicts, false, false);

        var arguments = NativeGit.WorktreeArguments(repo);
        arguments.AddRange(["diff", "--binary", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD", "--"]);
        var working = await NativeGit.ReadBytesAsync(repo.WorktreePath, arguments, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git could not capture working changes.");
        var pending = ReadPendingCommit(repo, kind);
        var finalControls = CaptureControls(repo, token);
        if (!string.Equals(kind, ReadKind(repo), StringComparison.Ordinal) || !controls!.AsSpan().SequenceEqual(finalControls))
            throw new InvalidDataException("Git operation changed during inspection.");

        using var revision = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, RevisionKey);
        revision.AppendData(finalControls);
        revision.AppendData(SHA256.HashData(working));
        revision.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { repo.WorktreePath, repo.GitDirectory, kind, status.Head, status.Entries }, WireJson.Options));
        return new(repo, kind!, status.Head, Convert.ToHexString(revision.GetHashAndReset()), conflicts, conflicts.Length == 0, true, pending);
    }

    private static string? ReadPendingCommit(GitWorktree repo, string? kind)
    {
        var commit = (kind switch
        {
            "rebase" => RelativeFile(repo, "REBASE_HEAD") ?? RelativeFile(repo, "rebase-merge/stopped-sha") ??
                RelativeFile(repo, "rebase-apply/original-commit"),
            "cherry-pick" => RelativeFile(repo, "CHERRY_PICK_HEAD"),
            "revert" => RelativeFile(repo, "REVERT_HEAD"),
            _ => null,
        })?.Trim();
        return GitHistoryOperations.IsObjectId(commit) ? commit : null;
    }

    private static byte[] CaptureControls(GitWorktree repo, CancellationToken token)
    {
        using var controls = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in ControlFiles)
        {
            token.ThrowIfCancellationRequested();
            var file = ReadBytes(Path.Combine(repo.GitDirectory, name));
            controls.AppendData(file is null ? [0] : [1]);
            if (file is not null)
                controls.AppendData(SHA256.HashData(file));
        }
        return controls.GetHashAndReset();
    }

    internal static async Task<FeatureOutcome> WithSelectionAsync(string path, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, Task<FeatureOutcome>> action, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection } git)
            return new("git_selection_required", "Select a Git worktree first.");
        if (!string.Equals(path, selection.Repository.WorktreePath, PathComparison))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!string.Equals(selection.ScopeRoot, selection.Repository.WorktreePath, PathComparison))
            return new("git_operation_scope_required", "Open the worktree root before managing Git operations.");
        token.ThrowIfCancellationRequested();
        return await TryAsync(() => action(git, selection)).ConfigureAwait(false);
    }

    internal static async Task<FeatureOutcome> TryAsync(Func<Task<FeatureOutcome>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new("git_operation_state_unavailable", "Git operation state could not be read consistently. Refresh status and retry.");
        }
    }

    internal static bool SameSelection(IWorkbenchRegistration view, GitSelection selection) =>
        view is IGitRegistration git && ReferenceEquals(git.SelectedGit, selection);

    internal static FeatureOutcome ChangedSelection() => new("git_selection_changed", "The selected Git worktree changed.");

    private static string? RelativeFile(GitWorktree repo, string name) => ReadFile(Path.Combine(repo.GitDirectory, name));
    private static string? ReadFile(string path) => ReadBytes(path) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    private static byte[]? ReadBytes(string path)
    {
        try { return GitIndexLock.Read(path); }
        catch (DirectoryNotFoundException) { return null; }
    }
}
