namespace RetroIde.Application.Workspaces;

internal sealed class PreparedDocumentSave(string temporaryPath, WorkspaceExplorer.SourceAddress source) : IDisposable
{
    private IDisposable? _publicationLease;

    public static async Task<PreparedDocumentSave> WriteAsync(DocumentSaveSnapshot snapshot, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            var path = ResolveDestination(snapshot.Source);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".retro-ide-save-{Guid.NewGuid():N}.tmp");
            var staged = new PreparedDocumentSave(temporary, snapshot.Source);
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var stream = new FileStream(temporary, options);
                await using var writer = new StreamWriter(stream, snapshot.Encoding);
                await writer.WriteAsync(snapshot.Text.AsMemory(), token).ConfigureAwait(false);
                await writer.FlushAsync(token).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                return staged;
            }
            catch
            {
                staged.Dispose();
                throw;
            }
        }, token).ConfigureAwait(false);
    }

    public void Commit()
    {
        var destination = ResolveDestination(source);
        File.Replace(temporaryPath, destination, destinationBackupFileName: null);
    }

    internal async Task WaitForPublicationAsync(CancellationToken token)
    {
        try { _publicationLease = await WorkspaceMutationGate.EnterAsync(token).ConfigureAwait(false); }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try { File.Delete(temporaryPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { Interlocked.Exchange(ref _publicationLease, null)?.Dispose(); }
    }

    private static string ResolveDestination(WorkspaceExplorer.SourceAddress address)
    {
        if (WorkspaceExplorer.TraversesLink(address.RootPath, address.Entry.RelativePath))
            throw new IOException("Source saves require an in-workspace file.");

        return Path.Combine(address.RootPath, address.Entry.RelativePath);
    }
}
