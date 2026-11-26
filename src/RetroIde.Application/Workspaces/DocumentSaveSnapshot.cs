using System.Text;

namespace RetroIde.Application.Workspaces;

internal sealed record DocumentSaveSnapshot(DocumentContext Context, WorkspaceExplorer.SourceAddress Source, string Text, Encoding Encoding);
