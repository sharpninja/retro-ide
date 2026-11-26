using RetroIde.Testing;

namespace RetroIde.GitFixture;

/// <summary>Verifies the native Git behavior used by the Source Control workbench tests.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos, ac-test-git-stage-and-push, ac-test-git-recovery. Establish native worktree, literal path, partial hunk, and conflict controls before the IDE binding.</remarks>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-stage-and-push, ac-test-git-recovery. Verify rejected push and merge/rebase recovery against real repositories.</remarks>
internal static class Program
{
    private static readonly StringComparer Paths =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Runs the local Git controls with an optional explicit executable.</summary>
    /// <param name="args">Zero or one Git executable name or absolute path.</param>
    /// <returns>Zero when every native control passes, or two for invalid arguments.</returns>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 1)
        {
            Console.Error.WriteLine("Usage: RetroIde.GitFixture [git-executable]");
            return 2;
        }

        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        using var fixture = await GitWorktreeFixture.CreateAsync(args.SingleOrDefault() ?? "git", limit.Token).ConfigureAwait(false);
        var version = await fixture.GitAsync(fixture.Primary, limit.Token, "--version").ConfigureAwait(false);
        Console.WriteLine(version.Trim());
        await VerifyWorktreesAsync(fixture, limit.Token).ConfigureAwait(false);
        await VerifyRenameAndLiteralPathsAsync(fixture, limit.Token).ConfigureAwait(false);
        await VerifyPartialHunksAsync(fixture, limit.Token).ConfigureAwait(false);
        await VerifyDivergenceAndAbortAsync(fixture, limit.Token).ConfigureAwait(false);
        Console.WriteLine("Native Git fixture passed worktree, literal-path, rename, hunk, and conflict-recovery checks.");
        return 0;
    }

    /// <summary>Verifies distinct staged and working states in two worktrees sharing one repository.</summary>
    /// <param name="fixture">The primary and linked checkouts.</param>
    /// <param name="token">Cancellation for native Git commands.</param>
    private static async Task VerifyWorktreesAsync(GitWorktreeFixture fixture, CancellationToken token)
    {
        var mainContext = await ReadContextAsync(fixture, fixture.Primary, token).ConfigureAwait(false);
        var linkedContext = await ReadContextAsync(fixture, fixture.Linked, token).ConfigureAwait(false);
        Require(Paths.Equals(mainContext.Common, linkedContext.Common) && !Paths.Equals(mainContext.Index, linkedContext.Index),
            "Linked repositories must share their object database and keep separate indexes.");

        var path = GitWorktreeFixture.ChangedPath;
        GitWorktreeFixture.Write(fixture.Primary, path, "staged text\n");
        await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "add", "--", path).ConfigureAwait(false);
        GitWorktreeFixture.Write(fixture.Primary, path, "working text\n");
        GitWorktreeFixture.Write(fixture.Linked, path, "linked text\n");

        var status = await fixture.GitAsync(fixture.Primary, token, "status", "--porcelain=v2", "-z", "--branch").ConfigureAwait(false);
        Require(status.Split('\0').Any(entry => entry.StartsWith("1 MM ", StringComparison.Ordinal) &&
            entry.EndsWith(" " + path, StringComparison.Ordinal)), "Primary status lost the literal Unicode path or index state.");
        var staged = await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "diff", "--cached", "--", path).ConfigureAwait(false);
        var working = await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "diff", "--", path).ConfigureAwait(false);
        var linkedIndex = await fixture.GitAsync(fixture.Linked, token, "--literal-pathspecs", "diff", "--cached", "--", path).ConfigureAwait(false);
        Require(staged.Contains("+staged text", StringComparison.Ordinal) &&
            working.Contains("+working text", StringComparison.Ordinal) && linkedIndex.Length == 0,
            "Staged, working, and linked states were not independently observable.");
        await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", path)
            .ConfigureAwait(false);
        Console.WriteLine("Linked worktrees share Git data and keep separate indexes and working diffs.");
    }

    /// <summary>Verifies native rename record framing and literal staging of an option-like path.</summary>
    /// <param name="fixture">The seeded checkout and independent linked worktree.</param>
    /// <param name="token">Cancellation for native Git commands.</param>
    private static async Task VerifyRenameAndLiteralPathsAsync(GitWorktreeFixture fixture, CancellationToken token)
    {
        const string Renamed = "src/Renamed 日本語 (draft) #2.cs";
        const string Literal = "-leading[1].asm";
        const string Lookalike = "-leading1.asm";
        var original = GitWorktreeFixture.ChangedPath;
        var repo = fixture.Primary;
        await fixture.GitAsync(repo, token, "mv", "--", original, Renamed).ConfigureAwait(false);
        var status = await fixture.GitAsync(repo, token, "status", "--porcelain=v2", "-z", "--branch").ConfigureAwait(false);
        var records = status.Split('\0');
        var rename = Array.FindIndex(records, item => item.StartsWith("2 R. ", StringComparison.Ordinal));
        Require(rename >= 0 && rename + 1 < records.Length && records[rename].EndsWith(" R100 " + Renamed, StringComparison.Ordinal) &&
            string.Equals(records[rename + 1], original, StringComparison.Ordinal),
            "Native porcelain lost the two literal path fields of the rename record.");
        await fixture.GitAsync(repo, token, "--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", original, Renamed)
            .ConfigureAwait(false);

        GitWorktreeFixture.Write(repo, Literal, "selected\n");
        GitWorktreeFixture.Write(repo, Lookalike, "neighbor\n");
        await fixture.GitAsync(repo, token, "--literal-pathspecs", "add", "--", Literal).ConfigureAwait(false);
        var staged = await fixture.GitAsync(repo, token, "diff", "--cached", "--name-only", "-z").ConfigureAwait(false);
        Require(string.Equals(staged, Literal + '\0', StringComparison.Ordinal), "Literal staging touched the pathspec lookalike.");
        await fixture.GitAsync(repo, token, "--literal-pathspecs", "restore", "--staged", "--", Literal).ConfigureAwait(false);
        File.Delete(Path.Combine(repo, Literal));
        File.Delete(Path.Combine(repo, Lookalike));
        Console.WriteLine("Native rename status retained original and destination paths. Literal staging excluded the neighboring name.");
    }

    /// <summary>Verifies selected hunk staging and unstaging, and rejects a patch whose index preimage changed.</summary>
    /// <param name="fixture">The seeded repository with a two-hunk source.</param>
    /// <param name="token">Cancellation for native Git commands.</param>
    private static async Task VerifyPartialHunksAsync(GitWorktreeFixture fixture, CancellationToken token)
    {
        const string First = "selected first edit";
        const string Last = "unstaged second edit";
        var repo = fixture.Primary;
        var path = GitWorktreeFixture.HunkPath;
        var original = GitWorktreeFixture.SeedHunks;
        var firstOnly = original.Replace("line 3\n", First + '\n', StringComparison.Ordinal);
        var working = firstOnly.Replace("line 17\n", Last + '\n', StringComparison.Ordinal);
        GitWorktreeFixture.Write(repo, path, working);
        var diff = await fixture.GitAsync(repo, token, "--literal-pathspecs", "diff", "--no-color", "--unified=1", "--", path)
            .ConfigureAwait(false);
        var firstHeader = diff.IndexOf("\n@@ ", StringComparison.Ordinal);
        var secondHeader = firstHeader < 0 ? -1 : diff.IndexOf("\n@@ ", firstHeader + 1, StringComparison.Ordinal);
        Require(secondHeader >= 0, "Native Git did not emit two independently selectable hunks.");
        var patch = fixture.WritePatch(diff[..(secondHeader + 1)]);

        await fixture.GitAsync(repo, token, "apply", "--cached", "--check", "--", patch).ConfigureAwait(false);
        await fixture.GitAsync(repo, token, "apply", "--cached", "--", patch).ConfigureAwait(false);
        var index = await fixture.GitAsync(repo, token, "show", ':' + path).ConfigureAwait(false);
        Require(string.Equals(index, firstOnly, StringComparison.Ordinal) &&
            string.Equals(await File.ReadAllTextAsync(Path.Combine(repo, path), token).ConfigureAwait(false), working, StringComparison.Ordinal),
            "Staging one hunk staged the second hunk or changed the worktree.");

        await fixture.GitAsync(repo, token, "apply", "--cached", "--reverse", "--check", "--", patch).ConfigureAwait(false);
        await fixture.GitAsync(repo, token, "apply", "--cached", "--reverse", "--", patch).ConfigureAwait(false);
        var unstaged = await fixture.GitAsync(repo, token, "show", ':' + path).ConfigureAwait(false);
        Require(string.Equals(unstaged, original, StringComparison.Ordinal), "Unstaging the selected hunk did not restore the index.");

        GitWorktreeFixture.Write(repo, path, original.Replace("line 3\n", "externally staged preimage\n", StringComparison.Ordinal));
        await fixture.GitAsync(repo, token, "--literal-pathspecs", "add", "--", path).ConfigureAwait(false);
        GitWorktreeFixture.Write(repo, path, working);
        var before = await fixture.GitAsync(repo, token, "write-tree").ConfigureAwait(false);
        var rejected = await fixture.RunAsync(repo, token, "apply", "--cached", "--check", "--", patch).ConfigureAwait(false);
        var after = await fixture.GitAsync(repo, token, "write-tree").ConfigureAwait(false);
        Require(rejected.ExitCode != 0 && rejected.Error.Contains("patch does not apply", StringComparison.Ordinal) &&
            string.Equals(before, after, StringComparison.Ordinal), "A stale hunk did not report its preimage mismatch or changed the index.");
        await fixture.GitAsync(repo, token, "--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", path)
            .ConfigureAwait(false);
        Console.WriteLine("Native staged and unstaged one hunk, then rejected the stale preimage without changing the index.");
    }

    /// <summary>Verifies native divergence, rejected push, conflict stages and abort isolation.</summary>
    /// <param name="fixture">The primary, linked and peer checkouts.</param>
    /// <param name="token">Cancellation for native Git commands.</param>
    private static async Task VerifyDivergenceAndAbortAsync(GitWorktreeFixture fixture, CancellationToken token)
    {
        var path = GitWorktreeFixture.ConflictPath;
        GitWorktreeFixture.Write(fixture.Primary, path, "local change\n");
        GitWorktreeFixture.Write(fixture.Peer, path, "peer change\n");
        await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "commit", "-m", "Local update", "--", path).ConfigureAwait(false);
        await fixture.GitAsync(fixture.Peer, token, "--literal-pathspecs", "commit", "-m", "Peer update", "--", path).ConfigureAwait(false);
        await fixture.GitAsync(fixture.Peer, token, "push", "origin", "main").ConfigureAwait(false);
        await fixture.GitAsync(fixture.Primary, token, "fetch", "origin").ConfigureAwait(false);

        var counts = await fixture.GitAsync(fixture.Primary, token, "rev-list", "--left-right", "--count", "HEAD...origin/main").ConfigureAwait(false);
        Require(string.Equals(counts.Trim(), "1\t1", StringComparison.Ordinal), "The isolated remote did not diverge.");
        var remoteTip = await fixture.GitAsync(fixture.Primary, token, "ls-remote", "--heads", "origin", "refs/heads/main").ConfigureAwait(false);
        var rejectedPush = await fixture.RunAsync(fixture.Primary, token, "push", "--porcelain", "origin", "main:main").ConfigureAwait(false);
        Require(rejectedPush.ExitCode != 0 && rejectedPush.Output.Contains("[rejected] (non-fast-forward)", StringComparison.Ordinal),
            "A divergent push did not report a non-fast-forward rejection.");
        var retainedTip = await fixture.GitAsync(fixture.Primary, token, "ls-remote", "--heads", "origin", "refs/heads/main").ConfigureAwait(false);
        Require(string.Equals(remoteTip, retainedTip, StringComparison.Ordinal), "The rejected push changed the bare remote tip.");

        var head = await fixture.GitAsync(fixture.Primary, token, "rev-parse", "HEAD").ConfigureAwait(false);
        var failure = await fixture.RunAsync(fixture.Primary, token, "merge", "--no-edit", "origin/main").ConfigureAwait(false);
        Require(failure.ExitCode == 1, "The conflicting merge did not return Git's conflict exit status.");
        var conflicted = await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "ls-files", "--unmerged", "-z", "--", path)
            .ConfigureAwait(false);
        Require(Enumerable.Range(1, 3).All(stage => conflicted.Contains($" {stage}\t{path}\0", StringComparison.Ordinal)),
            "Native Git did not report all three merge stages.");
        await fixture.GitAsync(fixture.Primary, token, "merge", "--abort").ConfigureAwait(false);
        await VerifyAbortedOperationAsync(fixture, head, token).ConfigureAwait(false);

        var rebase = await fixture.RunAsync(fixture.Primary, token, "rebase", "--merge", "origin/main").ConfigureAwait(false);
        Require(rebase.ExitCode == 1, "The conflicting rebase did not return Git's conflict exit status.");
        var rebaseState = await fixture.GitAsync(fixture.Primary, token, "rev-parse", "--path-format=absolute", "--git-path", "rebase-merge").ConfigureAwait(false);
        var rebasePath = rebaseState.TrimEnd('\r', '\n');
        Require(Directory.Exists(rebasePath), "Git did not expose the active rebase worktree state.");
        var rebasedConflict = await fixture.GitAsync(fixture.Primary, token, "--literal-pathspecs", "ls-files", "--unmerged", "-z", "--", path)
            .ConfigureAwait(false);
        Require(Enumerable.Range(1, 3).All(stage => rebasedConflict.Contains($" {stage}\t{path}\0", StringComparison.Ordinal)),
            "Native Git did not report all three rebase stages.");
        await fixture.GitAsync(fixture.Primary, token, "rebase", "--abort").ConfigureAwait(false);
        Require(!Directory.Exists(rebasePath), "Git retained the active rebase state after abort.");
        await VerifyAbortedOperationAsync(fixture, head, token).ConfigureAwait(false);
        Console.WriteLine("Native Git rejected the divergent push and recovered the primary checkout after merge and rebase conflicts.");
    }

    /// <summary>Checks the primary checkout's restored head and index and the linked checkout's original files.</summary>
    /// <param name="fixture">The primary and linked checkouts.</param>
    /// <param name="head">The primary commit before the aborted operation.</param>
    /// <param name="token">Cancellation for native Git commands.</param>
    private static async Task VerifyAbortedOperationAsync(GitWorktreeFixture fixture, string head, CancellationToken token)
    {
        var after = await fixture.GitAsync(fixture.Primary, token, "rev-parse", "HEAD").ConfigureAwait(false);
        var clean = await fixture.GitAsync(fixture.Primary, token, "status", "--porcelain=v2", "-z").ConfigureAwait(false);
        Require(string.Equals(head, after, StringComparison.Ordinal) && clean.Length == 0, "Aborting the operation did not restore the primary checkout.");
        Require(string.Equals(File.ReadAllText(Path.Combine(fixture.Linked, GitWorktreeFixture.ConflictPath)), GitWorktreeFixture.SeedConflict, StringComparison.Ordinal) &&
            string.Equals(File.ReadAllText(Path.Combine(fixture.Linked, GitWorktreeFixture.ChangedPath)), "linked text\n", StringComparison.Ordinal),
            "Recovery changed files in the linked checkout.");
    }

    /// <summary>Reads authoritative absolute Git-directory and index paths from the selected checkouts.</summary>
    /// <param name="fixture">The isolated native Git fixture.</param>
    /// <param name="directory">The target worktree.</param>
    /// <param name="token">Cancellation for the query.</param>
    /// <returns>The common Git directory and separate worktree index path.</returns>
    private static async Task<(string Common, string Index)> ReadContextAsync(GitWorktreeFixture fixture, string directory, CancellationToken token)
    {
        var output = await fixture.GitAsync(directory, token, "rev-parse", "--path-format=absolute", "--git-common-dir", "--git-path", "index")
            .ConfigureAwait(false);
        var paths = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Require(paths.Length == 2, "Git did not return both the common directory and index path.");
        return (Path.GetFullPath(paths[0]), Path.GetFullPath(paths[1]));
    }

    /// <summary>Fails the standalone fixture with the exact control that disagreed with native behavior.</summary>
    /// <param name="condition">Whether the check observed the expected result.</param>
    /// <param name="message">The observed failure's context.</param>
    /// <exception cref="InvalidOperationException">The observed result differs from the fixture expectation.</exception>
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
