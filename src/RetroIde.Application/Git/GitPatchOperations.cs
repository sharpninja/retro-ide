using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Exports native patch bytes and applies reviewed patches to one checkout layer.</summary>
internal static class GitPatchOperations
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    internal sealed record ExportInput(string WorktreePath, string Layer);
    internal sealed record ReviewInput(string WorktreePath, string PatchBase64, string Layer);
    internal sealed record ApplyInput(string WorktreePath, string PatchBase64, string Layer, string Revision);
    internal sealed record Export(string Layer, string PatchBase64, int Bytes);
    internal sealed record Review(string Layer, string Revision, string[] Paths, bool CanApply);
    internal sealed record Result(string Layer, string[] Paths);
    private sealed record Snapshot(Review Review, byte[]? Index);

    internal static Task<FeatureOutcome> ExportAsync(ExportInput input, IWorkbenchRegistration view, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, input.Layer, view, async (_, selection) =>
        {
            var repo = selection.Repository;
            var arguments = NativeGit.WorktreeArguments(repo);
            arguments.AddRange(["diff", "--binary", "--full-index", "--no-ext-diff", "--no-textconv", "--no-color",
                "--no-relative", "--src-prefix=a/", "--dst-prefix=b/"]);
            if (input.Layer is "index")
                arguments.Add("--cached");
            arguments.Add("--");
            var bytes = await NativeGit.ReadBytesAsync(repo.WorktreePath, arguments, token).ConfigureAwait(false);
            return bytes is null ? new("git_patch_export_failed", "Git could not export the selected diff.") :
                Publish(selection, "Git patch exported.", new Export(input.Layer, Convert.ToBase64String(bytes), bytes.Length));
        }, token);

    internal static Task<FeatureOutcome> ReviewAsync(ReviewInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithPatchAsync(input.WorktreePath, input.Layer, input.PatchBase64, view, async (_, selection, bytes) =>
        {
            var snapshot = await ReadSnapshotAsync(selection.Repository, input.Layer, bytes, token).ConfigureAwait(false);
            return snapshot is null ? Unreadable() : Publish(selection, snapshot.Review.CanApply ? "Git patch reviewed." :
                "The patch does not apply cleanly to the selected layer.", snapshot.Review);
        }, token);

    internal static Task<FeatureOutcome> ApplyAsync(ApplyInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithPatchAsync(input.WorktreePath, input.Layer, input.PatchBase64, view, async (git, selection, bytes) =>
        {
            var snapshot = await ReadSnapshotAsync(selection.Repository, input.Layer, bytes, token).ConfigureAwait(false);
            if (Validate(snapshot, input.Revision) is { } problem)
                return problem;
            return new FeatureOutcome("ok", "Git patch application prepared.")
            {
                Commit = current => GitOperationRecovery.SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Git patch application authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => GitOperationRecovery.TryAsync(
                            () => ExecuteAsync(git, selection, input, bytes, snapshot!.Review.Paths, cancellation)),
                    }
                    : GitOperationRecovery.ChangedSelection(),
            };
        }, token);

    private static async Task<FeatureOutcome> ExecuteAsync(IGitRegistration git, GitSelection selection, ApplyInput input, byte[] bytes,
        string[] paths, CancellationToken token)
    {
        using var held = await GitConfigurationGate.EnterAsync(selection.Repository, token).ConfigureAwait(false);
        var worktree = input.Layer is "worktree";
        var lease = await git.BeginIntegrationAsync(selection, worktree ? paths : [], token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return new(lease.ErrorCode ?? "git_patch_editors_busy", "Save affected unsaved editor changes before applying this patch.");
        var reload = false;
        try
        {
            var repo = selection.Repository;
            var snapshot = await ReadSnapshotAsync(repo, input.Layer, bytes, token).ConfigureAwait(false);
            if (Validate(snapshot, input.Revision) is { } problem)
                return problem;
            return await WithFileAsync(repo, bytes, async file =>
            {
                token.ThrowIfCancellationRequested();
                if (!worktree)
                    return await ApplyIndexAsync(repo, file, snapshot!, token).ConfigureAwait(false);
                var result = await RunApplyAsync(repo, file, "worktree", [], CancellationToken.None).ConfigureAwait(false);
                reload = true;
                return result.ExitCode == 0 ? FeatureOutcome.Success("Git patch applied to the worktree.", new Result(input.Layer, paths)) :
                    new("git_patch_apply_failed", "Git could not apply the patch. Refresh the review before retrying.");
            }, token).ConfigureAwait(false);
        }
        finally { await guard.CompleteAsync(reload).ConfigureAwait(false); }
    }

    private static async Task<FeatureOutcome> ApplyIndexAsync(GitWorktree repo, string file, Snapshot snapshot, CancellationToken token)
    {
        var stale = false;
        using var prepared = await PreparedGitIndex.PrepareAsync(repo, async index =>
        {
            stale = !GitIndexLock.ContentsEqual(snapshot.Index, GitIndexLock.Read(index));
            return !stale && (await RunApplyAsync(repo, file, "index", [], token, index).ConfigureAwait(false)).ExitCode == 0;
        }, token).ConfigureAwait(false);
        if (prepared is null)
            return stale ? Changed() : new("git_patch_apply_failed", "Git could not prepare the patch for the index.");
        token.ThrowIfCancellationRequested();
        return prepared.Publish() switch
        {
            "git_index_busy" => new("git_index_busy", "Another process owns the Git index lock."),
            "git_index_changed" => Changed(),
            _ => FeatureOutcome.Success("Git patch applied to the index.", new Result("index", snapshot.Review.Paths)),
        };
    }

    private static Task<Snapshot?> ReadSnapshotAsync(GitWorktree repo, string layer, byte[] bytes, CancellationToken token) =>
        WithFileAsync<Snapshot?>(repo, bytes, async file =>
        {
            var forward = RunApplyAsync(repo, file, layer, ["--numstat", "-z"], token);
            // Git reports the destination of renames and copies, so reverse stats supply the source paths.
            var reverse = RunApplyAsync(repo, file, layer, ["--reverse", "--numstat", "-z"], token);
            var stats = await Task.WhenAll(forward, reverse).ConfigureAwait(false);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (stats.Any(result => !AddPaths(result, paths)) || !paths.All(path => IsScoped(repo, path, layer)))
                return null;
            var ordered = paths.Order(StringComparer.Ordinal).ToArray();
            var check = await RunApplyAsync(repo, file, layer, ["--check"], token).ConfigureAwait(false);
            if (check.ExitCode != 0)
                return new Snapshot(new Review(layer, "", ordered, CanApply: false), Index: null);
            var index = layer is "index" ? GitIndexLock.Read(Path.Combine(repo.GitDirectory, "index")) : null;
            using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, RevisionKey);
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { repo.WorktreePath, repo.GitDirectory, layer, paths = ordered }, WireJson.Options));
            hash.AppendData(SHA256.HashData(bytes));
            if (layer is "index")
                hash.AppendData(SHA256.HashData(index ?? []));
            else
                foreach (var path in ordered)
                    await AppendPathStateAsync(hash, Path.GetFullPath(path, repo.WorktreePath), token).ConfigureAwait(false);
            var revision = Convert.ToHexString(hash.GetHashAndReset());
            return new Snapshot(new Review(layer, revision, ordered, CanApply: true), index);
        }, token);

    private static bool AddPaths(NativeGit.CommandResult stats, HashSet<string> paths)
    {
        if (stats.ExitCode != 0)
            return false;
        string output;
        try { output = new UTF8Encoding(false, true).GetString(stats.Output); }
        catch (DecoderFallbackException) { return false; }
        if (!output.EndsWith('\0'))
            return false;
        var fields = output.Split('\0');
        for (var i = 0; i < fields.Length - 1; i++)
        {
            var columns = fields[i].Split('\t', 3);
            if (columns.Length != 3)
                return false;
            if (columns[2].Length != 0)
                paths.Add(columns[2]);
            else
            {
                if (i + 2 >= fields.Length - 1)
                    return false;
                paths.Add(fields[++i]);
                paths.Add(fields[++i]);
            }
        }
        return true;
    }

    private static bool IsScoped(GitWorktree repo, string path, string layer)
    {
        if (Path.IsPathRooted(path) || (OperatingSystem.IsWindows() && path.Contains('\\')) ||
            path.Split('/').Any(part => part is "" or "." or ".." || string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)) ||
            !NativeGit.ContainsPath(repo.WorktreePath, Path.GetFullPath(path, repo.WorktreePath)))
            return false;
        return layer is "index" || Path.GetDirectoryName(path) is not { Length: > 0 } parent ||
            !WorkspaceExplorer.TraversesLink(repo.WorktreePath, parent);
    }

    private static async Task AppendPathStateAsync(IncrementalHash hash, string path, CancellationToken token)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is { } link)
        {
            hash.AppendData([1]);
            hash.AppendData(SHA256.HashData(Encoding.UTF8.GetBytes(link)));
            return;
        }
        if (!file.Exists)
        {
            hash.AppendData(Directory.Exists(path) ? [2] : [0]);
            return;
        }
        hash.AppendData([3]);
        if (!OperatingSystem.IsWindows())
            hash.AppendData(BitConverter.GetBytes((int)File.GetUnixFileMode(path)));
        using var stream = File.OpenRead(path);
        hash.AppendData(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    private static Task<NativeGit.CommandResult> RunApplyAsync(GitWorktree repo, string file, string layer, string[] flags,
        CancellationToken token, string? index = null)
    {
        var arguments = NativeGit.WorktreeArguments(repo);
        arguments.AddRange(["apply", "--whitespace=nowarn", .. flags]);
        if (layer is "index")
            arguments.Add("--cached");
        arguments.AddRange(["--", file]);
        return NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, token, index);
    }

    private static async Task<T> WithFileAsync<T>(GitWorktree repo, byte[] bytes, Func<string, Task<T>> action, CancellationToken token)
    {
        var file = Path.Combine(repo.GitDirectory, $"retro-ide-import-{Guid.NewGuid():N}.patch");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var stream = new FileStream(file, options);
            await using (stream.ConfigureAwait(false))
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            return await action(file).ConfigureAwait(false);
        }
        finally { File.Delete(file); }
    }

    private static Task<FeatureOutcome> WithPatchAsync(string path, string layer, string input, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, byte[], Task<FeatureOutcome>> action, CancellationToken token)
    {
        byte[] bytes;
        try { bytes = string.IsNullOrWhiteSpace(input) ? [] : Convert.FromBase64String(input); }
        catch (FormatException) { return Task.FromResult(Unreadable()); }
        return bytes.Length == 0 ? Task.FromResult(Unreadable()) :
            InScopeAsync(path, layer, view, (git, selection) => action(git, selection, bytes), token);
    }

    private static Task<FeatureOutcome> InScopeAsync(string path, string layer, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, Task<FeatureOutcome>> action, CancellationToken token) =>
        layer is not ("index" or "worktree") ? Task.FromResult(new FeatureOutcome("git_patch_layer_invalid", "Choose index or worktree.")) :
        GitOperationRecovery.WithSelectionAsync(path, view, async (git, selection) =>
        {
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            return found is not null && NativeGit.IsSameWorktree(found.Repository, selection.Repository)
                ? await action(git, selection).ConfigureAwait(false)
                : new("git_repository_unavailable", "The selected Git worktree is unavailable.");
        }, token);

    private static FeatureOutcome? Validate(Snapshot? snapshot, string expected) =>
        snapshot is null || !string.Equals(snapshot.Review.Revision, expected, StringComparison.Ordinal) ? Changed() :
        snapshot.Review.CanApply ? null : new("git_patch_apply_failed", "The patch does not apply cleanly to the selected layer.");
    private static FeatureOutcome Changed() => new("git_patch_changed", "The patch or checkout changed. Review it again before applying.");
    private static FeatureOutcome Unreadable() => new("git_patch_unreadable", "The input is not a readable patch for paths in the selected worktree.");
    private static FeatureOutcome Publish<T>(GitSelection selection, string message, T data) => new("ok", message)
    {
        Commit = current => GitOperationRecovery.SameSelection(current, selection)
            ? FeatureOutcome.Success(message, data) : GitOperationRecovery.ChangedSelection(),
    };
}
