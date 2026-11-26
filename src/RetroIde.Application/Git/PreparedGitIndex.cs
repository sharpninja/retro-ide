namespace RetroIde.Application.Git;

/// <summary>Prepares a whole-file update against a private Git index until CQRS authorizes publication.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-stage-and-push. Compare and replace only the selected worktree's native index.</remarks>
internal sealed class PreparedGitIndex : IDisposable
{
    private readonly string _index;
    private readonly string _temporary;
    private readonly byte[]? _original;
    private byte[]? _updated;

    private PreparedGitIndex(string index, byte[]? original)
    {
        _index = index;
        _original = original;
        _temporary = Path.Combine(Path.GetDirectoryName(index)!, $"retro-ide-index-{Guid.NewGuid():N}");
    }

    internal static async Task<PreparedGitIndex?> CreateAsync(GitWorktree repository, string[] paths, bool stage, CancellationToken token)
    {
        var index = Path.Combine(repository.GitDirectory, "index");
        var prepared = new PreparedGitIndex(index, ReadIndex(index));
        try
        {
            if (prepared._original is not null)
                await File.WriteAllBytesAsync(prepared._temporary, prepared._original, token).ConfigureAwait(false);
            var arguments = NativeGit.WorktreeArguments(repository);
            arguments.AddRange(stage ? ["add", "--"] : ["reset", "--quiet", "--"]);
            arguments.AddRange(paths);
            if (await NativeGit.RunAsync(repository.WorktreePath, arguments, token, indexPath: prepared._temporary).ConfigureAwait(false) is not null)
            {
                prepared._updated = ReadIndex(prepared._temporary);
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

    internal string? Publish()
    {
        var lockPath = _index + ".lock";
        FileStream stream;
        try { stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch (IOException) when (File.Exists(lockPath)) { return "git_index_busy"; }

        var ownsLock = true;
        try
        {
            using (stream)
            {
                var current = ReadIndex(_index);
                if (!ContentsEqual(current, _original))
                    return "git_index_changed";
                if (ContentsEqual(current, _updated))
                    return null;
                if (_updated is null)
                    throw new InvalidDataException("Git did not produce an index.");
                stream.Write(_updated);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && _original is not null)
                File.SetUnixFileMode(lockPath, File.GetUnixFileMode(_index));
            File.Move(lockPath, _index, overwrite: true);
            ownsLock = false;
            return null;
        }
        finally
        {
            if (ownsLock)
                File.Delete(lockPath);
        }
    }

    private static byte[]? ReadIndex(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
    }

    private static bool ContentsEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    public void Dispose()
    {
        File.Delete(_temporary);
        File.Delete(_temporary + ".lock");
    }
}
