namespace RetroIde.Application.Git;

/// <summary>Prepares an update against a private Git index until CQRS authorizes publication.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-stage-and-push. Compare and replace only the selected worktree's native index.</remarks>
internal sealed class PreparedGitIndex : IDisposable
{
    private readonly string _index;
    private readonly string _temporary;
    private readonly byte[]? _original;
    private byte[]? _updated;
    private IDisposable? _workspaceLease;

    private PreparedGitIndex(string index, byte[]? original)
    {
        _index = index;
        _original = original;
        _temporary = Path.Combine(Path.GetDirectoryName(index)!, $"retro-ide-index-{Guid.NewGuid():N}");
    }

    internal static Task<PreparedGitIndex?> CreateAsync(GitWorktree repository, string[] paths, bool stage, CancellationToken token) =>
        PrepareWithWorkspaceLeaseAsync(repository, async indexPath =>
        {
            var arguments = NativeGit.WorktreeArguments(repository);
            arguments.AddRange(stage ? ["add", "--"] : ["reset", "--quiet", "--"]);
            arguments.AddRange(paths);
            return await NativeGit.RunAsync(repository.WorktreePath, arguments, token, indexPath).ConfigureAwait(false) is not null;
        }, token);

    internal static async Task<PreparedGitIndex?> PrepareWithWorkspaceLeaseAsync(GitWorktree repository,
        Func<string, Task<bool>> update, CancellationToken token)
    {
        IDisposable? lease = await WorkspaceMutationGate.EnterAsync(token).ConfigureAwait(false);
        try
        {
            var current = await NativeGit.DiscoverAsync(repository.WorktreePath, "", token).ConfigureAwait(false);
            if (current is null || !NativeGit.IsSameWorktree(repository, current.Repository))
                return null;
            var prepared = await PrepareAsync(repository, update, token).ConfigureAwait(false);
            if (prepared is not null)
            {
                prepared._workspaceLease = lease;
                lease = null;
            }
            return prepared;
        }
        finally { lease?.Dispose(); }
    }

    internal static async Task<PreparedGitIndex?> PrepareAsync(GitWorktree repository, Func<string, Task<bool>> update, CancellationToken token)
    {
        var index = Path.Combine(repository.GitDirectory, "index");
        var prepared = new PreparedGitIndex(index, GitIndexLock.Read(index));
        try
        {
            if (prepared._original is not null)
                await File.WriteAllBytesAsync(prepared._temporary, prepared._original, token).ConfigureAwait(false);
            if (await update(prepared._temporary).ConfigureAwait(false))
            {
                prepared._updated = GitIndexLock.Read(prepared._temporary);
                return prepared;
            }
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
        prepared.Dispose();
        return null;
    }

    internal string? Publish(Func<bool>? verifyWorktree = null)
    {
        using var held = GitIndexLock.TryAcquire(_index);
        if (held is null)
            return "git_index_busy";
        var current = GitIndexLock.Read(_index);
        if (!GitIndexLock.ContentsEqual(current, _original))
            return "git_index_changed";
        if (verifyWorktree is not null && !verifyWorktree())
            return "git_diff_changed";
        if (GitIndexLock.ContentsEqual(current, _updated))
            return null;
        if (_updated is null)
            throw new InvalidDataException("Git did not produce an index.");
        held.Publish(_updated, _original is not null);
        return null;
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_temporary);
            File.Delete(_temporary + ".lock");
            File.Delete(_temporary + ".patch");
        }
        finally { Interlocked.Exchange(ref _workspaceLease, null)?.Dispose(); }
    }
}
