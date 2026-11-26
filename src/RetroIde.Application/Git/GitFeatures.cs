using System.ComponentModel;
using System.Text;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Registers scoped Git discovery, selection and status in the shared typed feature registry.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Prepare all native reads outside the workbench dispatcher and apply selection in CQRS.</remarks>
internal sealed class GitFeatures
{
    /// <summary>Addresses a repository candidate by an open root and a literal relative path.</summary>
    internal sealed record LocateInput(Guid RootId, string RelativePath);

    /// <summary>Requests status for the workbench's selected worktree.</summary>
    internal sealed record StatusInput;

    /// <summary>Resolves a workspace address to its native Git worktree without changing the selection.</summary>
    public Feature<LocateInput> Discover { get; } = new("git.repository.discover", "git_discover_repository",
        "Find the native Git worktree for a literal path inside an open workspace root.",
        static (input, registration, token) => LocateAsync(input, registration, select: false, token), typeof(GitWorktree));

    /// <summary>Selects the worktree identified by a captured workspace address.</summary>
    public Feature<LocateInput> Select { get; } = new("git.repository.select", "git_select_repository",
        "Select the native Git worktree for a path in the current workspace.",
        static (input, registration, token) => LocateAsync(input, registration, select: true, token), typeof(GitWorktree), mutation: true);

    /// <summary>Returns current machine-readable status for the selected worktree.</summary>
    public Feature<StatusInput> Status { get; } = new("git.status.read", "git_get_status", "Read the selected worktree's staged and working status.",
        static (_, view, token) => StatusAsync(view, token), typeof(GitStatus));

    /// <summary>Looks up a native repository and optionally applies it to the still-current workbench.</summary>
    private static async Task<FeatureOutcome> LocateAsync(LocateInput input, IWorkbenchRegistration view, bool select, CancellationToken token)
    {
        if (view is not IGitRegistration git)
            return new("git_unavailable", "Git is unavailable in this workbench.");
        WorkspaceExplorer.SourceAddress scope;
        try { scope = git.Explorer.ResolvePath(new(input.RootId, input.RelativePath)); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("git_scope_invalid", "Choose a literal path inside an open workspace root.");
        }

        GitSelection? located;
        try { located = await NativeGit.DiscoverAsync(scope.RootPath, scope.Entry.RelativePath, token).ConfigureAwait(false); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidOperationException)
        {
            return Unavailable();
        }
        if (located is null)
            return Unavailable();
        if (!select)
            return FeatureOutcome.Success("Git worktree discovered.", located.Repository);

        return new FeatureOutcome("ok", "Git worktree selected.")
        {
            Commit = current =>
            {
                ((IGitRegistration)current).SelectGit(located);
                return FeatureOutcome.Success("Git worktree selected.", located.Repository);
            },
        };
    }

    /// <summary>Reads the captured status and rejects it if the worktree selection changed before completion.</summary>
    private static async Task<FeatureOutcome> StatusAsync(IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        GitStatus? status;
        try { status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            return new("git_output_invalid", "Git did not return readable status records.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Unavailable();
        }
        if (status is null)
            return Unavailable();

        return new FeatureOutcome("ok", "Git status read.")
        {
            Commit = current => current is IGitRegistration git && ReferenceEquals(git.SelectedGit, selection)
                ? FeatureOutcome.Success("Git status read.", status)
                : new("git_selection_changed", "The selected Git worktree changed while status was read."),
        };
    }

    /// <summary>Returns the common result for a missing or unreadable worktree.</summary>
    private static FeatureOutcome Unavailable() => new("git_repository_unavailable", "The path has no available Git worktree.");
}
