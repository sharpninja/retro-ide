namespace RetroIde.Application.Git;

/// <summary>Owns the selected worktree's native index lock until it releases or replaces the index.</summary>
internal sealed class GitIndexLock : IDisposable
{
    private readonly string _index;
    private readonly string _path;
    private readonly FileStream _stream;
    private bool _published;

    private GitIndexLock(string index, string path, FileStream stream)
    {
        _index = index;
        _path = path;
        _stream = stream;
    }

    internal static GitIndexLock? TryAcquire(string index)
    {
        var lockPath = index + ".lock";
        try { return new(index, lockPath, new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)); }
        catch (IOException) when (File.Exists(lockPath)) { return null; }
    }

    internal void Publish(byte[] data, bool hasOriginal)
    {
        _stream.Write(data);
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        if (!OperatingSystem.IsWindows() && hasOriginal)
            File.SetUnixFileMode(_path, File.GetUnixFileMode(_index));
        File.Move(_path, _index, overwrite: true);
        _published = true;
    }

    public void Dispose()
    {
        _stream.Dispose();
        if (!_published)
            File.Delete(_path);
    }

    internal static byte[]? Read(string index)
    {
        try { return File.ReadAllBytes(index); }
        catch (FileNotFoundException) { return null; }
    }

    internal static bool ContentsEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
