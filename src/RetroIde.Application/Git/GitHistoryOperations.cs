using System.ComponentModel;
using System.Globalization;

namespace RetroIde.Application.Git;

/// <summary>Reads branch, worktree and commit data from the selected native Git repository.</summary>
internal static class GitHistoryOperations
{
    private const string CommitFields = "%H%x00%P%x00%an%x00%ae%x00%aI%x00%s";
    private const string RefFields = "%(refname)%00%(objectname)%00%(objecttype)%00%(*objectname)%00%(upstream)%00%(symref)%00";

    internal sealed record RefsInput(string WorktreePath);
    internal sealed record HistoryInput(string WorktreePath, string? RefName = null, string? Anchor = null, int Offset = 0, int Limit = 80);
    internal sealed record DetailsInput(string WorktreePath, string CommitId);
    internal sealed record CloseHistoryInput(Guid ViewKey);
    internal sealed record Reference(string Name, string ObjectId, string ObjectType, string? PeeledObjectId = null,
        string? Upstream = null, string? SymbolicTarget = null);
    internal sealed record Worktree(string Path, bool IsDetached, bool IsBare, bool IsLocked, bool IsPrunable,
        string? Head = null, string? Branch = null, bool IsPrimary = false, string? LockReason = null, string? PruneReason = null);
    internal sealed record RefState(GitWorktree Repository, Reference[] Refs, Worktree[] Worktrees);
    internal sealed record Commit(string Id, string[] Parents, string Author, string Email, string AuthoredAt, string Subject, string[] Refs);
    internal sealed record HistoryPage(GitWorktree Repository, int Offset, int Limit, bool HasMore, Commit[] Commits,
        string? RefName = null, string? Anchor = null);
    internal sealed record ChangedPath(string Status, string Path, string? OriginalPath = null);
    internal sealed record CommitDetails(GitWorktree Repository, Commit Commit, string Message, ChangedPath[] Changes, string? ComparedTo = null);
    internal sealed record HistoryTab(Guid ViewKey, Guid WorkspaceId, HistoryPage Page, RefState References);

    internal static Worktree? CurrentWorktree(RefState state)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Array.Find(state.Worktrees, tree => string.Equals(tree.Path, state.Repository.WorktreePath, comparison));
    }

    internal static Task<FeatureOutcome> RefsAsync(RefsInput input, IWorkbenchRegistration view, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var refs = await ReadRefsAsync(selection.Repository, token).ConfigureAwait(false);
            return Publish(selection, _ => FeatureOutcome.Success("Git references read.", refs));
        }, token);

    internal static Task<FeatureOutcome> HistoryAsync(HistoryInput input, IWorkbenchRegistration view, bool openTab, CancellationToken token)
    {
        if (input.Limit is < 1 or > 500 || input.Offset < 0 || (input.Anchor is not null && !IsObjectId(input.Anchor)) ||
            (input.Offset > 0 && input.Anchor is null))
            return Task.FromResult(new FeatureOutcome("git_history_invalid_page", "Choose a valid history page and supply its anchor for a nonzero offset."));

        return InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var repository = selection.Repository;
            var refs = await ReadRefsAsync(repository, token).ConfigureAwait(false);
            var reference = input.RefName is null ? null : Array.Find(refs.Refs,
                item => string.Equals(item.Name, input.RefName, StringComparison.Ordinal));
            if (input.RefName is not null && reference is null && input.Anchor is null)
                return new("git_ref_unavailable", "The specified ref is unavailable.");
            var anchor = await ResolveAnchorAsync(input, reference, repository, token).ConfigureAwait(false);
            if (anchor is null && (input.Anchor is not null || reference is not null))
                return new("git_history_unavailable", "The selected object does not resolve to a commit.");
            var page = await ReadHistoryAsync(repo: repository, input, anchor, refs, token).ConfigureAwait(false);
            return Publish(selection, git =>
            {
                if (!openTab)
                    return FeatureOutcome.Success("Git history read.", page);
                return git.ShowHistory(page, refs) is { } tab
                    ? FeatureOutcome.Success("Git history opened.", tab)
                    : new("view_closed", "The workbench cannot open a Git history tab.");
            });
        }, token);
    }

    internal static Task<FeatureOutcome> DetailsAsync(DetailsInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (!IsObjectId(input.CommitId))
            return Task.FromResult(new FeatureOutcome("git_commit_id_invalid", "Choose a full commit object ID."));

        return InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var repository = selection.Repository;
            var output = await RunAsync(repository, token, "log", "-z", "-1", "--no-notes", "--no-show-signature", "--no-decorate",
                "--no-color", "--encoding=UTF-8", $"--format={CommitFields}%x00%B", input.CommitId, "--").ConfigureAwait(false);
            if (output is null)
                return new("git_commit_unavailable", "Git could not read that commit.");
            var fields = SplitRecords(output, 7);
            if (fields.Length != 7 || !string.Equals(fields[0], input.CommitId, StringComparison.OrdinalIgnoreCase))
                return new("git_commit_unavailable", "The selected object is not a commit.");
            var commit = ReadCommit(fields, 0, []);
            var parent = commit.Parents.FirstOrDefault();
            var changes = parent is null
                ? await RunAsync(repository, token, "diff-tree", "--root", "--no-commit-id", "-r", "--name-status", "-z", "--find-renames", commit.Id, "--").ConfigureAwait(false)
                : await RunAsync(repository, token, "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--name-status", "-z",
                    "--find-renames", parent, commit.Id, "--").ConfigureAwait(false);
            if (changes is null)
                return new("git_commit_unavailable", "Git could not read changes for that commit.");
            var details = new CommitDetails(repository, commit, fields[6].TrimEnd('\r', '\n'), ReadChanges(changes), parent);
            return Publish(selection, _ => FeatureOutcome.Success("Git commit details read.", details));
        }, token);
    }

    internal static async Task<RefState> ReadRefsAsync(GitWorktree repository, CancellationToken token)
    {
        var refs = await RunAsync(repository, token, "for-each-ref", "--sort=refname", $"--format={RefFields}",
            "refs/heads", "refs/remotes", "refs/tags").ConfigureAwait(false);
        if (refs is null)
            throw new InvalidDataException("Git reference data is unavailable.");
        var refsFields = refs.Split('\0');
        if ((refsFields.Length - 1) % 6 != 0)
            throw new InvalidDataException("Git returned incomplete reference data.");
        var references = new List<Reference>();
        for (var i = 0; i < refsFields.Length - 1; i += 6)
        {
            references.Add(new(refsFields[i].TrimStart('\r', '\n'), refsFields[i + 1], refsFields[i + 2],
                Optional(refsFields[i + 3]), Optional(refsFields[i + 4]), Optional(refsFields[i + 5])));
        }

        var worktrees = await ReadWorktreesAsync(repository, token).ConfigureAwait(false);
        return new(repository, [.. references], worktrees);
    }

    internal static async Task<Worktree[]> ReadWorktreesAsync(GitWorktree repo, CancellationToken token)
    {
        var output = await RunAsync(repo, token, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false)
            ?? throw new InvalidDataException("Git worktree data is unavailable.");
        return output.Split("\0\0", StringSplitOptions.RemoveEmptyEntries).Select((record, index) =>
        {
            var fields = record.Split('\0');
            if (!fields[0].StartsWith("worktree ", StringComparison.Ordinal))
                throw new InvalidDataException("Git returned an invalid worktree record.");
            string? Value(string prefix) => fields.FirstOrDefault(field => field.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            bool Has(string name) => fields.Any(field => string.Equals(field, name, StringComparison.Ordinal) ||
                field.StartsWith(name + ' ', StringComparison.Ordinal));
            return new Worktree(Path.GetFullPath(fields[0]["worktree ".Length..]),
                Has("detached"), Has("bare"), Has("locked"), Has("prunable"), Value("HEAD "), Value("branch "),
                IsPrimary: index == 0, LockReason: Value("locked "), PruneReason: Value("prunable "));
        }).ToArray();
    }

    private static async Task<string?> ResolveAnchorAsync(HistoryInput input, Reference? reference, GitWorktree repo, CancellationToken token)
    {
        var result = await RunAsync(repo, token, "rev-parse", "--verify", "--quiet", "--end-of-options",
            (input.Anchor ?? reference?.ObjectId ?? "HEAD") + "^{commit}").ConfigureAwait(false);
        var anchor = result?.Trim();
        return IsObjectId(anchor) ? anchor : null;
    }

    private static async Task<HistoryPage> ReadHistoryAsync(GitWorktree repo, HistoryInput input, string? anchor, RefState refs, CancellationToken token)
    {
        if (anchor is null)
            return new(repo, input.Offset, input.Limit, false, [], input.RefName);
        var skip = input.Offset.ToString(CultureInfo.InvariantCulture);
        var count = (input.Limit + 1).ToString(CultureInfo.InvariantCulture);
        var output = await RunAsync(repo, token, "log", "-z", "--topo-order", "--no-color", "--no-show-signature", "--no-notes", "--no-decorate",
            "--encoding=UTF-8", $"--skip={skip}", $"--max-count={count}", $"--format={CommitFields}", anchor, "--").ConfigureAwait(false);
        if (output is null)
            throw new InvalidDataException("Git history is unavailable.");
        var fields = SplitRecords(output, 6);
        var decorations = refs.Refs.ToLookup(entry => entry.PeeledObjectId ?? entry.ObjectId, entry => entry.Name, StringComparer.Ordinal);
        var commits = new List<Commit>();
        for (var i = 0; i < Math.Min(fields.Length, input.Limit * 6); i += 6)
            commits.Add(ReadCommit(fields, i, [.. decorations[fields[i]]]));
        return new(repo, input.Offset, input.Limit, fields.Length / 6 > input.Limit, [.. commits], input.RefName, anchor);
    }

    private static Commit ReadCommit(string[] fields, int i, string[] refs) =>
        new(fields[i], fields[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries), fields[i + 2], fields[i + 3], fields[i + 4], fields[i + 5], refs);

    private static ChangedPath[] ReadChanges(string changes)
    {
        var fields = SplitRecords(changes, 1);
        var result = new List<ChangedPath>();
        for (var i = 0; i < fields.Length;)
        {
            var status = fields[i++];
            var rename = status.StartsWith('R') || status.StartsWith('C');
            if (i + (rename ? 1 : 0) >= fields.Length || status.Length == 0)
                throw new InvalidDataException("Git returned incomplete changed-file data.");
            var original = rename ? fields[i++] : null;
            result.Add(new(status, fields[i++], original));
        }
        return [.. result];
    }

    private static string[] SplitRecords(string output, int fields)
    {
        if (output.Length == 0)
            return [];
        var split = output.Split('\0');
        if (!output.EndsWith('\0') || (split.Length - 1) % fields != 0)
            throw new InvalidDataException("Git returned incomplete history data.");
        return split[..^1];
    }

    private static async Task<FeatureOutcome> InScopeAsync(string path, IWorkbenchRegistration view,
        Func<GitSelection, Task<FeatureOutcome>> operation, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(path, selection.Repository.WorktreePath, comparison))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!string.Equals(selection.ScopeRoot, selection.Repository.WorktreePath, comparison))
            return new("git_history_scope_required", "Open the whole worktree to read repository-wide history.");

        try
        {
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            if (found is null || !NativeGit.IsSameWorktree(found.Repository, selection.Repository))
                return new("git_repository_unavailable", "The Git worktree is no longer available.");
            return await operation(selection).ConfigureAwait(false);
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new("git_history_unavailable", "Git could not read repository history.");
        }
    }

    private static FeatureOutcome Publish(GitSelection selection, Func<IGitRegistration, FeatureOutcome> result) =>
        new("ok", "Git history read.")
        {
            Commit = current => current is IGitRegistration git && ReferenceEquals(git.SelectedGit, selection)
                ? result(git) : new("git_selection_changed", "The selected Git worktree changed."),
        };

    internal static bool IsObjectId(string? id) => id is { Length: 40 or 64 } && id.All(Uri.IsHexDigit);
    private static string? Optional(string field) => field.Length == 0 ? null : field;

    private static Task<string?> RunAsync(GitWorktree repo, CancellationToken token, params string[] commands)
    {
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(commands);
        return NativeGit.RunAsync(repo.WorktreePath, args, token);
    }
}
