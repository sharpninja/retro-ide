using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerSession
{
    private const string FolderChange = "workspace/didChangeWorkspaceFolders";
    private Folder[] _folders = [];
    private bool _folderChangeNotifications;
    private string? _folderRegistrationId;

    internal bool SupportsWorkspaceFolders { get; private set; }

    internal bool CanNotifyFolderChanges => SupportsWorkspaceFolders &&
        (_folderChangeNotifications || _folderRegistrationId is not null || IsRegistered(FolderChange));

    private void ReadFolderSupport(JsonElement capabilities)
    {
        if (!capabilities.TryGetProperty("workspace", out var workspace) || workspace.ValueKind != JsonValueKind.Object ||
            !workspace.TryGetProperty("workspaceFolders", out var folders) || folders.ValueKind != JsonValueKind.Object ||
            !TryTrue(folders, "supported"))
            return;

        SupportsWorkspaceFolders = true;
        if (folders.TryGetProperty("changeNotifications", out var notifications))
        {
            _folderChangeNotifications = notifications.ValueKind == JsonValueKind.True;
            _folderRegistrationId = notifications.ValueKind == JsonValueKind.String ? notifications.GetString() : null;
        }
    }

    internal async Task ChangeFolderAsync(LanguageServerConfiguration configuration, bool add, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        await _updates.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            if (!IsRunning || !CanNotifyFolderChanges)
                throw new IOException("The language server is unavailable for workspace folder updates.");
            var current = Volatile.Read(ref _folders);
            var folder = Folder.For(configuration);
            if (current.Any(item => item.RootId == folder.RootId) == add)
                return;
            if (!add)
            {
                foreach (var document in _documents.Where(item => item.Value.Context.RootId == folder.RootId))
                    await CloseCoreAsync(document.Key, deadline.Token).ConfigureAwait(false);
            }
            Folder[] next = add ? [.. current, folder] : current.Where(item => item.RootId != folder.RootId).ToArray();
            Volatile.Write(ref _folders, next);
            try
            {
                await _rpc.NotifyAsync(FolderChange, new
                {
                    @event = new
                    {
                        added = add ? [folder.Payload] : Array.Empty<object>(),
                        removed = add ? Array.Empty<object>() : [folder.Payload],
                    },
                }, deadline.Token).ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _folders, current);
                throw;
            }
        }
        finally { _updates.Release(); }
    }

    private bool ContainsRoot(Guid rootId) => Volatile.Read(ref _folders).Any(folder => folder.RootId == rootId);

    private object[] Folders() => Volatile.Read(ref _folders).Select(folder => folder.Payload).ToArray();

    private sealed record Folder(Guid RootId, string Uri, string Name)
    {
        internal static Folder For(LanguageServerConfiguration configuration) =>
            new(configuration.RootId, FileUri(configuration.Directory), new DirectoryInfo(configuration.Directory).Name);

        internal object Payload => new { uri = Uri, name = Name };
    }
}
