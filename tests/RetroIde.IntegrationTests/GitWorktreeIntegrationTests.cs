using Xunit;
using Xunit.Sdk;
using RetroIde.Testing;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises Git selection and status against independently seeded repositories and worktrees.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TR-RETRO-GIT-013#ac-git-machine-state. Share the exact assertions with the live workbench driver.</remarks>
public sealed partial class GitWorktreeIntegrationTests
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const string Renamed = "src/renamed (日本語) #2.asm";
    private const string Nested = "src/nested repository";
    private const string NestedFile = "only nested.asm";

    /// <summary>Verifies the expected behavior and that each independent routing or status fault is detected.</summary>
    /// <param name="fault">The reference fault to inject after the healthy control.</param>
    [Theory]
    [InlineData("shared-index")]
    [InlineData("wrong-nested-repository")]
    [InlineData("broken-rename")]
    [InlineData("accept-foreign-root")]
    public async Task Reference_detects_worktree_scope_faults(string fault)
    {
        using var sample = await CreateSampleAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await VerifyWorktreesAsync(new ReferenceGitPort(sample), sample).ConfigureAwait(false);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyWorktreesAsync(new ReferenceGitPort(sample, fault), sample)).ConfigureAwait(false);
    }

    /// <summary>Seeds separate staged and working states and an independent nested repository.</summary>
    /// <param name="token">Cancellation for the native fixture.</param>
    /// <returns>The initialized fixture, owned by the caller.</returns>
    private static async Task<GitWorktreeFixture> CreateSampleAsync(CancellationToken token)
    {
        var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(false);
        try
        {
            await fixture.GitAsync(fixture.Primary, token, "mv", "--", GitWorktreeFixture.ChangedPath, Renamed).ConfigureAwait(false);
            GitWorktreeFixture.Write(fixture.Primary, Renamed, "primary working change\n");
            GitWorktreeFixture.Write(fixture.Linked, GitWorktreeFixture.ChangedPath, "linked working change\n");
            var nested = Directory.CreateDirectory(Path.Combine(fixture.Primary, Nested)).FullName;
            await fixture.GitAsync(nested, token, "init", "--initial-branch=nested").ConfigureAwait(false);
            GitWorktreeFixture.Write(nested, NestedFile, "nested only\n");
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>Checks literal paths, checkout identity, an empty selection and the authorization boundary.</summary>
    /// <param name="port">The independent reference or live workbench adapter.</param>
    /// <param name="sample">The fixture supplying the expected native checkout paths.</param>
    private static async Task VerifyWorktreesAsync(IGitPort port, GitWorktreeFixture sample)
    {
        var mainRoot = await port.OpenRootAsync(sample.Primary);
        var linkedRoot = await port.OpenRootAsync(sample.Linked);
        var plain = await port.OpenRootAsync(sample.Remote);
        Assert.Equal("git_selection_required", await port.StatusCodeAsync());

        var main = await port.DiscoverAsync(mainRoot, Renamed);
        var linked = await port.DiscoverAsync(linkedRoot, GitWorktreeFixture.ChangedPath);
        Assert.Equal(sample.Primary, main.WorktreePath, PathComparer);
        Assert.Equal(sample.Linked, linked.WorktreePath, PathComparer);
        Assert.Equal(main.CommonDirectory, linked.CommonDirectory, PathComparer);
        Assert.NotEqual(main.GitDirectory, linked.GitDirectory, PathComparer);
        Assert.Equal("git_repository_unavailable", await port.DiscoveryCodeAsync(plain, ""));

        await port.SelectAsync(mainRoot, Renamed);
        var primaryStatus = await port.StatusAsync();
        Assert.Equal(main, primaryStatus.Repository);
        Assert.Equal("main", primaryStatus.Branch);
        var rename = Assert.Single(primaryStatus.Entries, entry => entry.Path == Renamed);
        Assert.Equal(new GitEntry(Renamed, GitWorktreeFixture.ChangedPath, "R", "M"), rename);

        await port.SelectAsync(linkedRoot, "");
        var linkedStatus = await port.StatusAsync();
        Assert.Equal(linked, linkedStatus.Repository);
        Assert.Equal("fixture-linked", linkedStatus.Branch);
        Assert.Equal(new GitEntry(GitWorktreeFixture.ChangedPath, null, ".", "M"), Assert.Single(linkedStatus.Entries));

        var nested = await port.DiscoverAsync(mainRoot, Nested + "/" + NestedFile);
        Assert.Equal(Path.GetFullPath(Path.Combine(sample.Primary, Nested)), nested.WorktreePath, PathComparer);
        Assert.NotEqual(main.CommonDirectory, nested.CommonDirectory, PathComparer);
        await port.SelectAsync(mainRoot, Nested);
        var nestedStatus = await port.StatusAsync();
        Assert.Equal(nested, nestedStatus.Repository);
        Assert.Equal("nested", nestedStatus.Branch);
        Assert.Equal(new GitEntry(NestedFile, null, "?", "?"), Assert.Single(nestedStatus.Entries));

        Assert.Equal("git_scope_invalid", await port.DiscoveryCodeAsync(Guid.NewGuid(), ""));
        Assert.Equal("git_scope_invalid", await port.DiscoveryCodeAsync(mainRoot, "../linked checkout"));
        Assert.Equal(nested, (await port.StatusAsync()).Repository);
    }

    private interface IGitPort
    {
        Task<Guid> OpenRootAsync(string path);
        Task<GitRepository> DiscoverAsync(Guid root, string relativePath);
        Task<string> DiscoveryCodeAsync(Guid root, string relativePath);
        Task SelectAsync(Guid root, string relativePath);
        Task<string> StatusCodeAsync();
        Task<GitView> StatusAsync();
    }

    private sealed record GitRepository(string WorktreePath, string GitDirectory, string CommonDirectory);
    private sealed record GitEntry(string Path, string? OriginalPath, string Index, string Worktree);
    private sealed record GitView(GitRepository Repository, string Branch, GitEntry[] Entries);

    private sealed class ReferenceGitPort(GitWorktreeFixture sample, string? fault = null) : IGitPort
    {
        private readonly Dictionary<Guid, string> _roots = [];
        private GitRepository? _selected;

        public Task<Guid> OpenRootAsync(string path)
        {
            var key = Guid.NewGuid();
            _roots.Add(key, path);
            return Task.FromResult(key);
        }

        public Task<GitRepository> DiscoverAsync(Guid root, string relativePath)
        {
            var path = _roots[root];
            var common = Path.Combine(sample.Primary, ".git");
            if (PathComparer.Equals(path, sample.Linked))
                return Task.FromResult(new GitRepository(path, fault == "shared-index" ? common : Path.Combine(common, "worktrees", "linked checkout"), common));
            if (relativePath.StartsWith(Nested, StringComparison.Ordinal) && fault != "wrong-nested-repository")
            {
                var nested = Path.GetFullPath(Path.Combine(path, Nested));
                return Task.FromResult(new GitRepository(nested, Path.Combine(nested, ".git"), Path.Combine(nested, ".git")));
            }
            return Task.FromResult(new GitRepository(path, common, common));
        }

        public Task<string> DiscoveryCodeAsync(Guid root, string relativePath)
        {
            if (!_roots.TryGetValue(root, out var path) || relativePath.StartsWith("..", StringComparison.Ordinal))
                return Task.FromResult(fault == "accept-foreign-root" ? "ok" : "git_scope_invalid");
            return Task.FromResult(PathComparer.Equals(path, sample.Remote) ? "git_repository_unavailable" : "ok");
        }

        public async Task SelectAsync(Guid root, string relativePath) => _selected = await DiscoverAsync(root, relativePath).ConfigureAwait(false);

        public Task<string> StatusCodeAsync() => Task.FromResult(_selected is null ? "git_selection_required" : "ok");

        public Task<GitView> StatusAsync()
        {
            var current = Assert.IsType<GitRepository>(_selected);
            if (PathComparer.Equals(current.WorktreePath, sample.Linked))
                return Task.FromResult(new GitView(current, "fixture-linked", [new(GitWorktreeFixture.ChangedPath, null, ".", "M")]));
            if (!PathComparer.Equals(current.WorktreePath, sample.Primary))
                return Task.FromResult(new GitView(current, "nested", [new(NestedFile, null, "?", "?")]));
            return Task.FromResult(new GitView(current, "main", [new(Renamed, fault == "broken-rename" ? null : GitWorktreeFixture.ChangedPath, "R", "M")]));
        }
    }
}
