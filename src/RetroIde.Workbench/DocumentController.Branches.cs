using Avalonia.Threading;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    private readonly HashSet<string> _switchingPaths = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _switchingWorktrees = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _switchingScopes = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal IGitBranchSwitch? BeginBranchSwitch(GitWorktree repo, string[]? relativePaths)
    {
        Dispatcher.UIThread.VerifyAccess();
        var root = repo.WorktreePath;
        var paths = relativePaths?.Select(path => Path.GetFullPath(path, root)).ToArray() ?? [];
        var wide = relativePaths is null;
        if (_switchingWorktrees.Contains(root) || paths.Any(IsSwitchingPath) ||
            (wide && _switchingWorktrees.Any(other => NativeGit.ContainsPath(root, other) || NativeGit.ContainsPath(other, root))))
            return null;

        var checkedPaths = paths.ToHashSet(_switchingPaths.Comparer);
        var documents = _open.Values.Where(document => document.Source is { } view &&
            (wide ? NativeGit.ContainsPath(root, view.FullPath) : checkedPaths.Contains(view.FullPath))).ToArray();
        if (documents.Any(document => document.Source!.IsDirty || _interaction.IsBlocked(document.Source.Scope)))
            return null;

        _switchingPaths.UnionWith(paths);
        _switchingWorktrees.Add(root);
        if (wide)
            _switchingScopes.Add(root);
        foreach (var document in documents)
            _interaction.Begin(document.Source!.Scope);
        return new BranchSwitch(this, root, paths, documents);
    }

    private bool IsSwitchingPath(string path) => _switchingPaths.Contains(path) ||
        _switchingScopes.Any(root => NativeGit.ContainsPath(root, path));

    private sealed class BranchSwitch(DocumentController owner, string worktree, string[] paths, ShellDocument[] documents) : IGitBranchSwitch
    {
        public async Task CompleteAsync(bool switched)
        {
            try
            {
                if (switched)
                {
                    var updated = await Task.WhenAll(documents.Select(async document =>
                        (Document: document, Source: await WorkspaceExplorer.ReadSourceAsync(document.Source!.SourceAddress, CancellationToken.None)
                            .ConfigureAwait(false)))).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var (document, source) in updated)
                        {
                            var view = document.Source!;
                            if (!ReferenceEquals(owner.GetView<IView>(view.ViewKey), view))
                                continue;
                            if (source is not null)
                                view.ReloadSource(source);
                            else
                                owner.CloseAfterBranchSwitch(document);
                        }
                    });
                }
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    owner._switchingPaths.ExceptWith(paths);
                    owner._switchingWorktrees.Remove(worktree);
                    owner._switchingScopes.Remove(worktree);
                    foreach (var document in documents)
                        owner._interaction.End(document.Source!.Scope);
                });
            }
        }
    }

    private void CloseAfterBranchSwitch(ShellDocument document)
    {
        var previous = _closingView;
        _closingView = document.Source!.ViewKey;
        try { _layout.Factory.CloseDockable(document); }
        finally { _closingView = previous; }
    }
}
