using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Checks that Git selection and file status retain their workspace and worktree identity.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos, ac-test-git-extensions.</remarks>
public sealed partial class GitWorkspaceStateTests
{
    private static readonly StringComparer NativePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Proves the reopen expectations detect a checkout incorrectly selected from the Explorer row.</summary>
    [Fact]
    public void Reference_detects_confusion_between_Explorer_and_Git_selection()
    {
        var workspace = Guid.NewGuid();
        var explorerRoot = Guid.NewGuid();
        var linked = Path.GetFullPath("linked checkout");
        var primary = Path.GetFullPath("primary checkout");
        VerifySelection(new Selection(workspace, explorerRoot, linked), workspace, explorerRoot, linked);
        Assert.ThrowsAny<XunitException>(() => VerifySelection(new Selection(workspace, explorerRoot, primary), workspace, explorerRoot, linked));
    }

    /// <summary>Proves the decoration expectations detect a staged change leaking into another checkout.</summary>
    [Fact]
    public void Reference_detects_decorations_leaking_to_a_linked_worktree()
    {
        var expected = new Badges("Tracked", "Staged, Modified", "Modified", "Untracked", "Ignored", "");
        VerifyBadges(expected);
        Assert.ThrowsAny<XunitException>(() => VerifyBadges(expected with { Linked = "Staged, Modified" }));
    }

    private static void VerifySelection(Selection actual, Guid workspace, Guid explorer, string? worktree)
    {
        Assert.Equal(workspace, actual.Workspace);
        Assert.Equal(explorer, actual.ExplorerRoot);
        Assert.Equal(worktree, actual.Worktree, NativePaths);
    }

    private static void VerifyBadges(Badges actual)
    {
        Assert.Equal("Tracked", actual.Clean);
        Assert.Equal("Staged, Modified", actual.StagedAndModified);
        Assert.Equal("Modified", actual.Modified);
        Assert.Equal("Untracked", actual.Untracked);
        Assert.Equal("Ignored", actual.Ignored);
        Assert.Empty(actual.Linked);
    }

    private sealed record Selection(Guid Workspace, Guid ExplorerRoot, string? Worktree);
    private sealed record Badges(string Clean, string StagedAndModified, string Modified, string Untracked, string Ignored, string Linked);
}
