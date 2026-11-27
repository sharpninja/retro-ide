using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.Git;

/// <summary>Manages the shared native stash stack from the selected worktree.</summary>
internal static class GitStash
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    internal enum Action { Apply, Pop, Drop }
    internal sealed record ListInput(string WorktreePath);
    internal sealed record CreateInput(string WorktreePath, string Message, bool IncludeUntracked = false);
    internal sealed record EntryInput(string WorktreePath, string Revision, string Selector, string ObjectId, bool RestoreIndex = false);
    internal sealed record Entry(string Selector, string ObjectId, string Message);
    internal sealed record Stack(GitWorktree Repository, string Revision, Entry[] Entries);
    internal sealed record Inspection(Entry Entry, string Patch);
    internal sealed record Result(Stack Stack, string[] Conflicts, bool Applied = false);

    internal static Task<FeatureOutcome> ListAsync(ListInput input, IWorkbenchRegistration view, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var stack = await ReadStackAsync(selection.Repository, token).ConfigureAwait(false);
            return Publish(selection, "Git stashes read.", stack);
        }, token);

    internal static Task<FeatureOutcome> InspectAsync(EntryInput input, IWorkbenchRegistration view, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var repo = selection.Repository;
            var entry = Find(await ReadStackAsync(repo, token).ConfigureAwait(false), input);
            if (entry is null)
                return Changed();
            var patch = await ReadAsync(repo, token, "stash", "show", "--include-untracked", "--patch", "--binary",
                "--no-ext-diff", "--no-textconv", "--no-color", entry.ObjectId).ConfigureAwait(false);
            return Publish(selection, "Git stash inspected.", new Inspection(entry, patch));
        }, token);

    internal static Task<FeatureOutcome> CreateAsync(CreateInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Contains('\0'))
            return Task.FromResult(new FeatureOutcome("git_stash_message_required", "Enter a valid stash message."));
        return PrepareAsync(input.WorktreePath, view, (git, selection, cancellation) => CreateCoreAsync(git, selection, input, cancellation), token);
    }

    internal static Task<FeatureOutcome> ChangeAsync(EntryInput input, IWorkbenchRegistration view, Action action, CancellationToken token) =>
        PrepareAsync(input.WorktreePath, view, (git, selection, cancellation) => ChangeCoreAsync(git, selection, input, action, cancellation), token);

    private static Task<FeatureOutcome> PrepareAsync(string path, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, CancellationToken, Task<FeatureOutcome>> execute, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(path, view, (git, selection) => Task.FromResult(new FeatureOutcome("ok", "Git stash operation prepared.")
        {
            Commit = current => GitOperationRecovery.SameSelection(current, selection)
                ? new FeatureOutcome("ok", "Git stash operation authorized.")
                {
                    ExecuteAuthorizedAsync = cancellation => GitOperationRecovery.TryAsync(
                        () => GitMutationLease.RunAsync(selection.Repository, cancellation, () => execute(git, selection, cancellation))),
                }
                : GitOperationRecovery.ChangedSelection(),
        }), token);

    private static async Task<FeatureOutcome> CreateCoreAsync(IGitRegistration git, GitSelection selection, CreateInput input, CancellationToken token)
    {
        var repo = selection.Repository;
        var before = await StatusAsync(selection, token).ConfigureAwait(false);
        if (before.OperationKind is not null || before.Entries.Any(entry => entry.IsUnmerged))
            return IntegrationInProgress();
        var paths = ChangedPaths(before, input.IncludeUntracked);
        if (paths.Length == 0)
            return new("git_stash_no_changes", "There are no selected changes to stash.");
        var lease = await git.BeginIntegrationAsync(selection, paths, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return Busy(lease);
        var invoked = false;
        try
        {
            var current = await StatusAsync(selection, token).ConfigureAwait(false);
            if (current.OperationKind is not null || current.Entries.Any(entry => entry.IsUnmerged))
                return IntegrationInProgress();
            if (!paths.SequenceEqual(ChangedPaths(current, input.IncludeUntracked), StringComparer.Ordinal))
                return new("git_stash_worktree_changed", "The changed paths changed while preparing this stash. Review the checkout again.");
            var arguments = new List<string> { "stash", "push", "--message", input.Message };
            if (input.IncludeUntracked)
                arguments.Add("--include-untracked");
            token.ThrowIfCancellationRequested();
            invoked = true;
            var result = await RunAsync(repo, CancellationToken.None, [.. arguments]).ConfigureAwait(false);
            return await FinishAsync(selection, result.ExitCode == 0 ? "ok" : "git_stash_failed",
                result.ExitCode == 0 ? "Git stash created." : "Git could not stash the changes. Review the checkout and stash stack.").ConfigureAwait(false);
        }
        finally { await guard.CompleteAsync(invoked).ConfigureAwait(false); }
    }

    private static async Task<FeatureOutcome> ChangeCoreAsync(IGitRegistration git, GitSelection selection, EntryInput input,
        Action action, CancellationToken token)
    {
        var repo = selection.Repository;
        var lease = await git.BeginIntegrationAsync(selection, action is Action.Drop ? [] : null, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return Busy(lease);
        var invoked = false;
        try
        {
            var status = await StatusAsync(selection, token).ConfigureAwait(false);
            var stack = await ReadStackAsync(repo, token).ConfigureAwait(false);
            var entry = Find(stack, input);
            if (entry is null)
                return Changed();
            if (action is Action.Drop)
                return await DropAsync(selection, input, applied: false, token).ConfigureAwait(false);
            if (status.OperationKind is not null || status.Entries.Any(item => item.IsUnmerged))
                return IntegrationInProgress();
            if (await WouldOverwriteLocalAsync(repo, status, entry, token).ConfigureAwait(false))
                return new("git_stash_local_changes", "Move the untracked or ignored paths that this stash would overwrite.");
            string[] arguments = input.RestoreIndex ? ["stash", "apply", "--index", entry.ObjectId] : ["stash", "apply", entry.ObjectId];
            token.ThrowIfCancellationRequested();
            invoked = true;
            var native = await RunAsync(repo, CancellationToken.None, arguments).ConfigureAwait(false);
            if (native.ExitCode != 0)
            {
                var conflict = (await StatusAsync(selection, CancellationToken.None).ConfigureAwait(false)).Entries.Any(item => item.IsUnmerged);
                return await FinishAsync(selection, conflict ? "git_stash_conflict" : "git_stash_failed",
                    conflict ? "Git applied changes with conflicts and kept the stash." : "Git could not apply the stash. Review the selected checkout.")
                    .ConfigureAwait(false);
            }
            return action is Action.Pop
                ? await DropAsync(selection, input, applied: true, CancellationToken.None).ConfigureAwait(false)
                : await FinishAsync(selection, "ok", "Git stash applied.", applied: true).ConfigureAwait(false);
        }
        finally { await guard.CompleteAsync(invoked).ConfigureAwait(false); }
    }

    private static async Task<FeatureOutcome> DropAsync(GitSelection selection, EntryInput input, bool applied, CancellationToken token)
    {
        var repo = selection.Repository;
        var entry = Find(await ReadStackAsync(repo, token).ConfigureAwait(false), input);
        if (entry is null)
            return applied ? await FinishAsync(selection, "git_stash_applied_retained",
                "Git applied the stash. Review the changed stack before dropping the entry.", applied: true).ConfigureAwait(false) : Changed();
        token.ThrowIfCancellationRequested();
        var drop = await RunAsync(repo, CancellationToken.None, "stash", "drop", entry.Selector).ConfigureAwait(false);
        if (drop.ExitCode != 0)
            return await FinishAsync(selection, applied ? "git_stash_applied_retained" : "git_stash_failed",
                applied ? "Git applied the stash. The entry was not dropped." : "Git could not drop the reviewed stash.", applied).ConfigureAwait(false);
        return await FinishAsync(selection, "ok", applied ? "Git stash popped." : "Git stash dropped.", applied).ConfigureAwait(false);
    }

    private static async Task<FeatureOutcome> FinishAsync(GitSelection selection, string code, string summary, bool applied = false)
    {
        var stack = await ReadStackAsync(selection.Repository, CancellationToken.None).ConfigureAwait(false);
        var status = await StatusAsync(selection, CancellationToken.None).ConfigureAwait(false);
        var conflicts = status.Entries.Where(item => item.IsUnmerged).Select(item => item.Path).ToArray();
        return new(code, summary, JsonSerializer.SerializeToElement(new Result(stack, conflicts, applied), WireJson.Options));
    }

    private static async Task<bool> WouldOverwriteLocalAsync(GitWorktree repo, GitStatus status, Entry entry, CancellationToken token)
    {
        var local = status.Entries.Where(item => item.Worktree is "?" or "!").Select(item => item.Path.TrimEnd('/')).ToArray();
        if (local.Length == 0)
            return false;
        var paths = await ReadAsync(repo, token, "stash", "show", "--name-only", "-z", "--include-untracked", "--format=", entry.ObjectId)
            .ConfigureAwait(false);
        return GitOperationRecovery.Collides(paths.Split('\0', StringSplitOptions.RemoveEmptyEntries), local);
    }

    private static async Task<Stack> ReadStackAsync(GitWorktree repo, CancellationToken token)
    {
        var raw = await ReadAsync(repo, token, "stash", "list", "-z", "--format=%gd%x00%H%x00%gs").ConfigureAwait(false);
        var fields = raw.Split('\0');
        if (fields[^1].Length != 0 || (fields.Length - 1) % 3 != 0)
            throw new InvalidDataException("Git returned an invalid stash list.");
        var entries = new List<Entry>();
        for (var i = 0; i < fields.Length - 1; i += 3)
        {
            if (!fields[i].StartsWith("stash@{", StringComparison.Ordinal) || !GitHistoryOperations.IsObjectId(fields[i + 1]))
                throw new InvalidDataException("Git returned an invalid stash selector or object ID.");
            entries.Add(new Entry(fields[i], fields[i + 1], fields[i + 2]));
        }
        var bytes = Encoding.UTF8.GetBytes(repo.WorktreePath + "\0" + repo.CommonDirectory + "\0" + raw);
        return new Stack(repo, Convert.ToHexString(HMACSHA256.HashData(RevisionKey, bytes)), [.. entries]);
    }

    private static Entry? Find(Stack stack, EntryInput input) => string.Equals(stack.Revision, input.Revision, StringComparison.Ordinal)
        ? Array.Find(stack.Entries, entry => string.Equals(entry.Selector, input.Selector, StringComparison.Ordinal) &&
            string.Equals(entry.ObjectId, input.ObjectId, StringComparison.OrdinalIgnoreCase))
        : null;

    private static string[] ChangedPaths(GitStatus status, bool includeUntracked) =>
        [.. status.Entries.Where(item => item.Worktree is not "!" && (includeUntracked || item.Worktree is not "?"))
            .SelectMany(item => item.OriginalPath is { } old ? new[] { item.Path, old } : [item.Path])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static async Task<GitStatus> StatusAsync(GitSelection selection, CancellationToken token) =>
        await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false) ?? throw new InvalidDataException("Git status is unavailable.");
    private static async Task<string> ReadAsync(GitWorktree repo, CancellationToken token, params string[] command) =>
        await NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. command], token).ConfigureAwait(false)
        ?? throw new InvalidDataException("Git could not read the stash stack.");
    private static Task<NativeGit.CommandResult> RunAsync(GitWorktree repo, CancellationToken token, params string[] command)
    {
        var arguments = NativeGit.WorktreeArguments(repo);
        // Stash cleanup uses its own top-level pathspec. These commands pass no caller pathspecs.
        arguments.Remove("--literal-pathspecs");
        arguments.AddRange(["-c", "submodule.recurse=false", "-c", "stash.index=false", .. command]);
        return NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, token);
    }

    private static FeatureOutcome Publish<T>(GitSelection selection, string message, T data) => new("ok", message)
    {
        Commit = current => GitOperationRecovery.SameSelection(current, selection) ? FeatureOutcome.Success(message, data) :
            GitOperationRecovery.ChangedSelection(),
    };
    private static FeatureOutcome Changed() => new("git_stash_changed", "The stash stack changed. Refresh and review the entry again.");
    private static FeatureOutcome IntegrationInProgress() =>
        new("git_operation_in_progress", "Finish or recover the active Git operation and conflicts before changing working files with stash.");
    private static FeatureOutcome Busy(GitIntegrationLease lease) =>
        new(lease.ErrorCode ?? "git_stash_editors_busy", "The checkout is busy or an affected editor has unsaved changes.");
}
