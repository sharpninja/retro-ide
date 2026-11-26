using RetroIde.Testing;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Proves native Git setup, workspace registration and preservation of unrelated files.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
public sealed partial class GitRepositorySetupTests
{
    private static readonly StringComparer NativePath = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Proves the setup expectations reject missing workspace or rollback behavior.</summary>
    [Theory]
    [InlineData("missing-workspace-root")]
    [InlineData("failed-clone-residue")]
    public async Task Reference_detects_setup_faults(string fault)
    {
        var token = TestContext.Current.CancellationToken;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await VerifyProvisionAsync(new NativeReference(sample, token), sample.Remote).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyProvisionAsync(new NativeReference(sample, token, fault), sample.Remote)).ConfigureAwait(true);
    }

    /// <summary>Runs the same filesystem, checkout and workspace expectations against either setup implementation.</summary>
    private static async Task VerifyProvisionAsync(IProvisionPort port, string remote)
    {
        var scratch = Directory.CreateTempSubdirectory("retro-ide-provision-");
        try
        {
            var existing = scratch.CreateSubdirectory("existing source – café").FullName;
            var original = Path.Combine(existing, "keep.txt");
            File.WriteAllText(original, "existing data");
            Assert.Equal("ok", await port.InitializeAsync(existing).ConfigureAwait(true));
            Assert.True(Directory.Exists(Path.Combine(existing, ".git")));
            Assert.Equal("existing data", File.ReadAllText(original));
            await VerifySelectedAsync(port, existing, hasCommit: false).ConfigureAwait(true);
            Assert.Equal("ok", await port.InitializeAsync(existing).ConfigureAwait(true));

            var created = Path.Combine(scratch.FullName, "new repository");
            Assert.Equal("ok", await port.InitializeAsync(created).ConfigureAwait(true));
            await VerifySelectedAsync(port, created, hasCommit: false).ConfigureAwait(true);

            var clone = Path.Combine(scratch.FullName, "cloned checkout");
            Assert.Equal("ok", await port.CloneAsync(remote, clone).ConfigureAwait(true));
            await VerifySelectedAsync(port, clone, hasCommit: true).ConfigureAwait(true);
            Assert.True(File.Exists(Path.Combine(clone, GitWorktreeFixture.ChangedPath)));

            var empty = scratch.CreateSubdirectory("empty destination").FullName;
            Assert.Equal("ok", await port.CloneAsync(remote, empty).ConfigureAwait(true));
            await VerifySelectedAsync(port, empty, hasCommit: true).ConfigureAwait(true);

            Assert.Equal("git_destination_not_empty", await port.CloneAsync(remote, existing).ConfigureAwait(true));
            Assert.Equal("existing data", File.ReadAllText(original));
            var failed = Path.Combine(scratch.FullName, "failed clone");
            Assert.Equal("git_clone_failed", await port.CloneAsync(Path.Combine(scratch.FullName, "no such remote"), failed).ConfigureAwait(true));
            Assert.False(Path.Exists(failed));
            Assert.Equal(4, scratch.EnumerateFileSystemInfos().Count());
            Assert.Equal(4, (await port.RootsAsync().ConfigureAwait(true)).Distinct(NativePath).Count());
            await VerifySelectedAsync(port, empty, hasCommit: true).ConfigureAwait(true);
        }
        finally { scratch.Delete(recursive: true); }
    }

    private static async Task VerifySelectedAsync(IProvisionPort port, string path, bool hasCommit)
    {
        Assert.Contains(path, await port.RootsAsync().ConfigureAwait(true), NativePath);
        var status = await port.StatusAsync().ConfigureAwait(true);
        Assert.Equal(path, status.Path, NativePath);
        Assert.Equal(hasCommit, !string.IsNullOrEmpty(status.Head));
    }

    private interface IProvisionPort
    {
        Task<string> InitializeAsync(string destination);
        Task<string> CloneAsync(string source, string destination);
        Task<string[]> RootsAsync();
        Task<ProvisionStatus> StatusAsync();
    }

    private sealed record ProvisionStatus(string Path, string? Head);

    private sealed class NativeReference(GitWorktreeFixture git, CancellationToken token, string? fault = null) : IProvisionPort
    {
        private readonly HashSet<string> _roots = new(NativePath);
        private string? _active;

        public async Task<string> InitializeAsync(string destination)
        {
            await git.GitAsync(git.Primary, token, "init", "--", destination).ConfigureAwait(false);
            return Register(destination);
        }

        public async Task<string> CloneAsync(string source, string destination)
        {
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                return "git_destination_not_empty";
            if (!Directory.Exists(source))
            {
                if (string.Equals(fault, "failed-clone-residue", StringComparison.Ordinal))
                    Directory.CreateDirectory(destination);
                return "git_clone_failed";
            }
            await git.GitAsync(git.Primary, token, "clone", "--", source, destination).ConfigureAwait(false);
            return Register(destination);
        }

        public Task<string[]> RootsAsync() => Task.FromResult(_roots.ToArray());

        public async Task<ProvisionStatus> StatusAsync()
        {
            var active = Assert.IsType<string>(_active);
            var status = await git.GitAsync(active, token, "status", "--porcelain=v2", "--branch").ConfigureAwait(false);
            var oid = status.Split('\n').First(line => line.StartsWith("# branch.oid ", StringComparison.Ordinal))["# branch.oid ".Length..].Trim();
            return new(active, string.Equals(oid, "(initial)", StringComparison.Ordinal) ? null : oid);
        }

        private string Register(string path)
        {
            _active = path;
            if (!string.Equals(fault, "missing-workspace-root", StringComparison.Ordinal))
                _roots.Add(path);
            return "ok";
        }
    }
}
