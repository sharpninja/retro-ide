namespace RetroIde.Application.Workspaces;

internal sealed record DocumentContext(Guid ViewKey, Guid WorkspaceId, Guid DocumentId, Guid RootId,
    string RelativePath, long Revision);
