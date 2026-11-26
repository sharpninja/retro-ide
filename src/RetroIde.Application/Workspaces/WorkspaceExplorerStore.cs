using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RetroIde.Application.Workspaces;

internal sealed class WorkspaceExplorerStore(string path, ILogger logger)
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _path = Path.GetFullPath(path);

    public WorkspaceExplorerStore(string path) : this(path, NullLogger.Instance) { }

    public WorkspaceExplorer? Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumBytes)
                return null;

            var state = JsonSerializer.Deserialize<WorkspaceExplorer.State>(stream, WireJson.Options);
            return state is null ? null : WorkspaceExplorer.Restore(state);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            RuntimeFailures.Report(logger, error, "saved workspace load; using an empty workspace", LogLevel.Warning);
            return null;
        }
    }

    public void Save(WorkspaceExplorer.State state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, WireJson.Options);
        if (bytes.Length > MaximumBytes)
            throw new IOException("The workspace Explorer state exceeds its storage limit.");

        var folder = Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName;
        var temporary = Path.Combine(folder, $".workspace-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
