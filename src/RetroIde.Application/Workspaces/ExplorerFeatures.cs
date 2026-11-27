namespace RetroIde.Application.Workspaces;

internal sealed class ExplorerFeatures(Func<DocumentSaveSnapshot, CancellationToken, Task<PreparedDocumentSave>>? stageSave = null)
{
    internal sealed record OpenRootInput(string Path, string Kind);
    internal sealed record ReadRootsInput;
    internal sealed record ReadChildrenInput(Guid RootId, string RelativePath);
    internal sealed record SetExpansionInput(Guid RootId, string RelativePath, bool IsExpanded);
    internal sealed record SelectEntryInput(Guid RootId, string RelativePath);
    internal sealed record OpenEntryInput(Guid RootId, string RelativePath, SourcePosition? Position = null);
    internal sealed record RevealEntryInput(Guid RootId, string RelativePath);
    internal sealed record SaveDocumentInput(DocumentContext Document);

    public Feature<OpenRootInput> OpenRoot { get; } = new(
        "workspace.root.open", "workspace_open_root", "Open an absolute directory as a project or folder root.", true,
        AddRoot, typeof(WorkspaceExplorer.Root));

    public Feature<ReadRootsInput> ReadRoots { get; } = new(
        "workspace.roots.read", "workspace_list_roots", "List the selected workspace's roots and stable IDs.", false,
        static (_, view) => view is IExplorerRegistration { HasExplorer: true } explorer
            ? FeatureOutcome.Success("Workspace roots read.", explorer.Explorer.Roots)
            : Unavailable(), typeof(WorkspaceExplorer.Root[]));

    public Feature<ReadChildrenInput> ReadChildren { get; } = new(
        "workspace.children.read", "workspace_list_children", "List one directory relative to an open workspace root.",
        ReadBranchAsync, typeof(WorkspaceExplorer.Branch));

    public Feature<SetExpansionInput> SetExpansion { get; } = new(
        "workspace.expansion.set", "workspace_set_expanded", "Set whether a directory is expanded in the Project Explorer.", true,
        SetExpanded);

    public Feature<SelectEntryInput> SelectEntry { get; } = new(
        "workspace.entry.select", "workspace_select_entry", "Select a root-relative entry in the Project Explorer.", true,
        Select);

    public Feature<OpenEntryInput> OpenEntry { get; } = new(
        "workspace.entry.open", "workspace_open_file", "Open or activate a root-relative file in the source editor.",
        PrepareOpenAsync, typeof(DocumentContext), mutation: true);

    public Feature<RevealEntryInput> RevealEntry { get; } = new(
        "workspace.entry.reveal", "workspace_reveal_entry", "Expand Project Explorer ancestors and reveal a root-relative entry.", true,
        Reveal);

    /// <summary>Saves the captured document and returns the outcome to its registered originating view.</summary>
    /// <remarks>
    /// 2026-11-26 · TEST-RETRO-011#ac-test-save-source, TEST-RETRO-007#ac-test-controller-ownership.
    /// A staged write failure remains associated with the captured document when another tab becomes active.
    /// </remarks>
    public Feature<SaveDocumentInput> SaveDocument { get; } = new(
        "document.source.save", "editor_save_document", "Save the captured text buffer of a registered document at its expected revision.",
        (input, view, token) => PrepareSaveAsync(input, view, token, stageSave ?? PreparedDocumentSave.WriteAsync),
        typeof(DocumentContext), mutation: true);

    private static async Task<FeatureOutcome> PrepareSaveAsync(SaveDocumentInput input, IWorkbenchRegistration view, CancellationToken token,
        Func<DocumentSaveSnapshot, CancellationToken, Task<PreparedDocumentSave>> stageSave)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (explorer.CaptureDocumentSave(input.Document) is not { } snapshot)
            return new("document_changed", "The document is unavailable or its revision has changed.");

        PreparedDocumentSave staged;
        try
        {
            staged = await stageSave(snapshot, token).ConfigureAwait(false);
            await staged.WaitForPublicationAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Text.EncoderFallbackException)
        {
            return new("save_failed", "Unable to write the source file.") { KeepCapturedTarget = true };
        }

        return new FeatureOutcome("ok", "Save prepared.")
        {
            PreparedResource = staged,
            KeepCapturedTarget = true,
            Commit = registration => CommitSave(registration, snapshot, staged),
        };
    }

    private static FeatureOutcome CommitSave(IWorkbenchRegistration view, DocumentSaveSnapshot snapshot, PreparedDocumentSave staged)
    {
        try
        {
            return view is IExplorerRegistration { HasExplorer: true } explorer &&
                explorer.CommitDocumentSave(snapshot, staged) is { } document
                ? FeatureOutcome.Success("Source saved.", document)
                : new("view_closed", "The source tab is no longer available.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new("save_failed", "Unable to replace the source file.");
        }
    }

    private static async Task<FeatureOutcome> PrepareOpenAsync(OpenEntryInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (!explorer.CanChangeExplorer)
            return new("explorer_busy", "The Project Explorer is currently blocked.");

        if (input.Position is { } position && (position.Line < 1 || position.Column < 1))
            return new("invalid_location", "Line and column numbers start at 1.");

        WorkspaceExplorer.SourceAddress address;
        try { address = explorer.Explorer.ResolveSource(new(input.RootId, input.RelativePath)); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("invalid_entry", "The file must be relative to an open workspace root.");
        }

        WorkspaceExplorer.SourceFile? source = null;
        if (!explorer.HasOpenDocument(address.Entry))
        {
            source = await WorkspaceExplorer.ReadSourceAsync(address, token).ConfigureAwait(false);
            if (source is null)
                return new("source_unavailable", "The source file is unavailable or cannot be read as text.");
        }

        return new FeatureOutcome("ok", "Source prepared.")
        {
            Commit = registration => registration is IExplorerRegistration { HasExplorer: true, CanChangeExplorer: true } live &&
                live.OpenOrActivateDocument(address.Entry, source, input.Position) is { } document
                    ? FeatureOutcome.Success("Editor tab activated.", document)
                    : new("editor_unavailable", "The editor tab is no longer available."),
        };
    }

    private static FeatureOutcome Reveal(RevealEntryInput input, IWorkbenchRegistration view)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (!explorer.CanChangeExplorer)
            return new("explorer_busy", "The Project Explorer is currently blocked.");

        try
        {
            var entry = explorer.Explorer.ResolveSource(new(input.RootId, input.RelativePath));
            explorer.Explorer.Reveal(entry.Entry);
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("invalid_entry", "The entry must be relative to an open workspace root.");
        }

        explorer.PublishExplorer();
        view.TryFocus("explorer");
        return new("ok", "Entry revealed in Project Explorer.");
    }

    private static FeatureOutcome Select(SelectEntryInput input, IWorkbenchRegistration view)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (!explorer.CanChangeExplorer)
            return new("explorer_busy", "The Project Explorer is currently blocked.");

        bool changed;
        try { changed = explorer.Explorer.Select(new(input.RootId, input.RelativePath)); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("invalid_entry", "The entry must be relative to an open workspace root.");
        }

        if (changed)
            explorer.PublishExplorer();
        return new("ok", "Explorer selection updated.");
    }

    private static FeatureOutcome SetExpanded(SetExpansionInput input, IWorkbenchRegistration view)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (!explorer.CanChangeExplorer)
            return new("explorer_busy", "The Project Explorer is currently blocked.");

        bool changed;
        try { changed = explorer.Explorer.SetExpanded(new(input.RootId, input.RelativePath), input.IsExpanded); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("invalid_directory", "The directory must be relative to an open workspace root.");
        }

        if (changed)
            explorer.PublishExplorer();
        return new("ok", input.IsExpanded ? "Directory expanded." : "Directory collapsed.");
    }

    private static async Task<FeatureOutcome> ReadBranchAsync(ReadChildrenInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();

        var branch = await explorer.Explorer.ReadChildrenAsync(new(input.RootId, input.RelativePath), token).ConfigureAwait(false);
        return FeatureOutcome.Success(branch.IsAvailable ? "Directory read." : "The directory is unavailable.", branch);
    }

    private static FeatureOutcome AddRoot(OpenRootInput input, IWorkbenchRegistration view)
    {
        if (view is not IExplorerRegistration { HasExplorer: true } explorer)
            return Unavailable();
        if (!explorer.CanChangeExplorer)
            return new("explorer_busy", "The Project Explorer is currently blocked.");
        var kind = input.Kind switch
        {
            "folder" => WorkspaceExplorer.RootKind.Folder,
            "project" => WorkspaceExplorer.RootKind.Project,
            _ => (WorkspaceExplorer.RootKind?)null,
        };
        if (kind is null)
            return new("invalid_root_kind", "Use folder or project as the root kind.");

        WorkspaceExplorer.Root root;
        try { root = explorer.Explorer.AddRoot(input.Path, kind.Value); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("invalid_root_path", "Use a valid absolute directory for the workspace root.");
        }

        explorer.PublishExplorer();
        return FeatureOutcome.Success("Workspace root opened.", root);
    }

    private static FeatureOutcome Unavailable() => new("explorer_unavailable", "The Project Explorer is unavailable in this workspace.");
}
