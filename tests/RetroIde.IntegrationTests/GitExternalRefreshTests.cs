using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Checks that worktree data and successive revisions update the desktop in order.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-extensions; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
public sealed partial class GitWorkspaceStateTests
{
    /// <summary>Proves the assertions catch stale checkout state and a superseded index refresh.</summary>
    [Fact]
    public void Reference_detects_stale_external_Git_refreshes()
    {
        var linked = Path.GetFullPath("linked checkout");
        var primary = Path.GetFullPath("primary checkout");
        var current = new RefreshState(linked, "fixture-linked", "MM");
        VerifyRefresh(current, linked, "fixture-linked", "MM");
        Assert.ThrowsAny<XunitException>(() => VerifyRefresh(current with { Worktree = primary }, linked, "fixture-linked", "MM"));
        Assert.ThrowsAny<XunitException>(() => VerifyRefresh(current with { Status = ".M" }, linked, "fixture-linked", "MM"));
    }

    private static void VerifyRefresh(RefreshState actual, string worktree, string branch, string status)
    {
        Assert.Equal(worktree, actual.Worktree, NativePaths);
        Assert.Equal(branch, actual.Branch);
        Assert.Equal(status, actual.Status);
    }

    private sealed record RefreshState(string Worktree, string Branch, string Status);
}
