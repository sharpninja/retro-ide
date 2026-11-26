using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises the Source Control workflow with two checkouts and a layout saved before the pane existed.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Run the same checks through the reference and live workbench.</remarks>
public sealed partial class GitSourceControlTests
{
    private const string Pane = "source-control";
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Proves the workbench checks distinguish independent selection and index groups.</summary>
    [Theory]
    [InlineData("stale-pane")]
    [InlineData("merged-index")]
    public async Task Reference_detects_source_control_projection_faults(string fault)
    {
        using var sample = await CreateSourceControlSampleAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await VerifySurfaceAsync(new ReferenceSurface(sample), sample).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifySurfaceAsync(new ReferenceSurface(sample, fault), sample)).ConfigureAwait(true);
    }

    /// <summary>Proves the expected migrated layout catches a reset and a missing addition.</summary>
    [Fact]
    public void Reference_detects_layout_reset_and_missing_pane()
    {
        var legacy = LegacyLayout();
        var migrated = legacy with { Main = legacy.Main with { Pinned = [.. legacy.Main.Pinned, new(Pane, "Left")] } };
        VerifyMigration(migrated);
        Assert.ThrowsAny<XunitException>(() => VerifyMigration(legacy));
        Assert.ThrowsAny<XunitException>(() => VerifyMigration(migrated with { ActiveTool = null }));
    }

    /// <summary>Seeds independent changes in the worktree and index so the UI cannot conflate them.</summary>
    private static async Task<GitWorktreeFixture> CreateSourceControlSampleAsync(CancellationToken token)
    {
        var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(false);
        try
        {
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "staged on main\n");
            await sample.GitAsync(sample.Primary, token, "add", "--", GitWorktreeFixture.ChangedPath).ConfigureAwait(false);
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.HunkPath, "working on main\n");
            GitWorktreeFixture.Write(sample.Linked, GitWorktreeFixture.ConflictPath, "working on linked\n");
            return sample;
        }
        catch
        {
            sample.Dispose();
            throw;
        }
    }

    /// <summary>Checks that desktop selection and MCP selection both update the same visible pane.</summary>
    private static async Task VerifySurfaceAsync(ISourceControlSurface surface, GitWorktreeFixture sample)
    {
        await surface.SelectFromExplorerAsync().ConfigureAwait(true);
        var primary = await surface.ReadAsync().ConfigureAwait(true);
        Assert.Equal(sample.Primary, primary.Path, Paths);
        Assert.Equal("main", primary.Branch);
        Assert.Equal(new[] { GitWorktreeFixture.ChangedPath }, primary.Staged);
        Assert.Equal(new[] { GitWorktreeFixture.HunkPath }, primary.Working);

        await surface.SelectOverMcpAsync().ConfigureAwait(true);
        var linked = await surface.ReadAsync().ConfigureAwait(true);
        Assert.Equal(sample.Linked, linked.Path, Paths);
        Assert.Equal("fixture-linked", linked.Branch);
        Assert.Empty(linked.Staged);
        Assert.Equal(new[] { GitWorktreeFixture.ConflictPath }, linked.Working);
        Assert.Equal(sample.Linked, await surface.ReadMcpSelectionAsync().ConfigureAwait(true), Paths);
    }

    /// <summary>Creates a complete version-one document layout with a pinned pane and one floating window.</summary>
    private static WorkbenchLayoutState LegacyLayout() => new(1,
        new DockBranch(new DockNode("document-tabs", "documents", Active: "emulation", Children:
            [new("document", "start"), new("document", "emulation"), new("document", "disassembly"), new("document", "test-results")]),
            [new("explorer", "Left", "left"), new("call-stack", "Right", "right")]),
        [new DockBranch(new DockNode("tool-tabs", "previously-floating", Active: "problems", Children:
            [new("tool", "registers"), new("tool", "output"), new("tool", "problems"), new("tool", "serial"),
             new("tool", "breakpoints"), new("tool", "mcp-activity")], Alignment: "Right"), [], 544, 338)], "problems");

    /// <summary>Requires new pane discovery while retaining the original document and every existing Dock placement.</summary>
    private static void VerifyMigration(WorkbenchLayoutState state)
    {
        Assert.Equal("problems", state.ActiveTool);
        Assert.Equal("document-tabs", state.Main.Content.Kind);
        Assert.Equal("emulation", state.Main.Content.Active);
        Assert.Contains(state.Main.Pinned, pin => pin is { Tool: "explorer", Alignment: "Left" });
        Assert.Contains(state.Main.Pinned, pin => pin is { Tool: "call-stack", Alignment: "Right" });
        Assert.Single(state.Main.Pinned, pin => pin is { Tool: Pane, Alignment: "Left" });
        var floating = Assert.Single(state.Floating);
        Assert.Equal("previously-floating", floating.Content.Id);
        Assert.Equal("problems", floating.Content.Active);
        Assert.Equal(544, floating.Width);
        Assert.Equal(338, floating.Height);
    }

    private interface ISourceControlSurface
    {
        Task SelectFromExplorerAsync();
        Task SelectOverMcpAsync();
        Task<PaneSnapshot> ReadAsync();
        Task<string> ReadMcpSelectionAsync();
    }

    private sealed record PaneSnapshot(string Path, string Branch, string[] Staged, string[] Working);

    private sealed class ReferenceSurface(GitWorktreeFixture sample, string? fault = null) : ISourceControlSurface
    {
        private bool _linked;

        public Task SelectFromExplorerAsync()
        {
            _linked = false;
            return Task.CompletedTask;
        }

        public Task SelectOverMcpAsync()
        {
            _linked = true;
            return Task.CompletedTask;
        }

        public Task<PaneSnapshot> ReadAsync()
        {
            if (_linked && !string.Equals(fault, "stale-pane", StringComparison.Ordinal))
                return Task.FromResult(new PaneSnapshot(sample.Linked, "fixture-linked", [], [GitWorktreeFixture.ConflictPath]));
            string[] state = string.Equals(fault, "merged-index", StringComparison.Ordinal)
                ? [GitWorktreeFixture.ChangedPath, GitWorktreeFixture.HunkPath]
                : [GitWorktreeFixture.HunkPath];
            return Task.FromResult(new PaneSnapshot(sample.Primary, "main", [GitWorktreeFixture.ChangedPath], state));
        }

        public Task<string> ReadMcpSelectionAsync() => Task.FromResult(_linked ? sample.Linked : sample.Primary);
    }
}
