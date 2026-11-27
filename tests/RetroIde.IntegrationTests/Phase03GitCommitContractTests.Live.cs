using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;
using GitFixture = RetroIde.IntegrationTests.Phase03OperationContractTests;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase03GitCommitContractTests
{
    /// <summary>Applies the reference index and hook assertions through the live MCP workbench.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-013#ac-test-git-stage-and-push. Observes native HEAD, index, and working bytes after the reviewed CQRS operation.</remarks>
    [AvaloniaFact]
    public Task Live_workbench_publishes_only_the_reviewed_index() => WithLiveCommitPortAsync(VerifyReviewedCommitAsync);

    /// <summary>Requires a fresh review when a configured pre-commit hook restages working changes.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
    [AvaloniaFact]
    public Task Live_workbench_rejects_changes_restaged_by_a_native_hook() => WithLiveCommitPortAsync(
        async (port, token) =>
        {
            var before = await port.ReadStateAsync(token).ConfigureAwait(false);
            var reviewed = await port.ReviewAsync(token).ConfigureAwait(false);
            await port.ConfigureRestagingHookAsync(token).ConfigureAwait(false);

            var result = await port.CommitAsync(reviewed, token).ConfigureAwait(false);
            Assert.Equal(before, await port.ReadStateAsync(token).ConfigureAwait(false));
            Assert.Equal("git_commit_state_changed", result);
        });

    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Live_review_guards_unborn_and_detached_head(bool detached) => WithLiveCommitPortAsync(
        async (port, token) =>
        {
            var before = await port.ReadCommitBoundaryAsync(token).ConfigureAwait(false);
            Assert.Equal(detached ? "(detached)" : "main", before.Branch);
            Assert.Equal(detached ? before.MainTip : null, before.Head);
            Assert.Equal(Staged, before.IndexText);
            Assert.Equal(Dirty, before.WorktreeText);
            var reviewed = await port.ReviewAsync(token).ConfigureAwait(false);
            await port.ConfigureRestagingHookAsync(token).ConfigureAwait(false);

            Assert.Equal("git_commit_state_changed", await port.CommitAsync(reviewed, token).ConfigureAwait(false));
            Assert.Equal(before, await port.ReadCommitBoundaryAsync(token).ConfigureAwait(false));

            await port.ConfigureHookAsync(allow: true, token).ConfigureAwait(false);
            Assert.Equal("ok", await port.CommitAsync(reviewed, token).ConfigureAwait(false));
            var committed = await port.ReadStateAsync(token).ConfigureAwait(false);
            var after = await port.ReadCommitBoundaryAsync(token).ConfigureAwait(false);
            Assert.NotEqual(before.Head, committed.Head);
            Assert.Equal(before.Head, committed.Parent);
            Assert.Equal(Staged, committed.CommittedText);
            Assert.Equal(before with { Head = committed.Head, MainTip = detached ? before.MainTip : committed.Head }, after);
        }, detached ? FixtureHead.Detached : FixtureHead.Unborn);

    private enum FixtureHead { Branch, Unborn, Detached }

    private sealed record CommitBoundary(string Branch, string? Head, string MainTip, string IndexText, string WorktreeText);

    private static async Task WithLiveCommitPortAsync(Func<LiveCommitPort, CancellationToken, Task> exercise,
        FixtureHead head = FixtureHead.Branch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var folder = Directory.CreateTempSubdirectory("retro-commit-contract-");
        try
        {
            await VerifyWorkbenchAsync(folder, exercise, head, timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => file.IsReadOnly))
                    file.IsReadOnly = false;
            folder.Delete(recursive: true);
        }
    }

    private static async Task VerifyWorkbenchAsync(DirectoryInfo folder, Func<LiveCommitPort, CancellationToken, Task> exercise,
        FixtureHead head, CancellationToken token)
    {
        var session = new WorkbenchSession(Guid.NewGuid());
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token)
                .ConfigureAwait(true);
            var grant = host.CreateAttachment("phase03-reviewed-commit", canMutate: true);
            using var http = grant.CreateHttpClient();
            await using var direct = await GitFixture.ConnectAsync(grant, http, token).ConfigureAwait(true);
            var tools = await direct.ListToolsAsync(cancellationToken: token).ConfigureAwait(true);
            Assert.True(tools.Any(tool => string.Equals(tool.Name, "git_get_commit_state", StringComparison.Ordinal)),
                "The live workbench does not expose git_get_commit_state.");
            await using var bridge = await GitFixture.ConnectStdioAsync(folder, grant, token).ConfigureAwait(true);

            var checkout = folder.CreateSubdirectory("checkout").FullName;
            var hooks = folder.CreateSubdirectory("hooks").FullName;
            var port = new LiveCommitPort(bridge, checkout, hooks);
            await port.ConfigureFixtureAsync(head, token).ConfigureAwait(true);
            await exercise(port, token).ConfigureAwait(true);
        }
        finally { await GitFixture.DisposeWorkbenchAsync(session).ConfigureAwait(true); }
    }

    private sealed class LiveCommitPort(McpClient client, string checkout, string hooks) : IReviewedCommitPort
    {
        private const string Source = "contract.asm";
        private string WorkingFile => Path.Combine(checkout, Source);

        internal async Task ConfigureFixtureAsync(FixtureHead head, CancellationToken token)
        {
            await GitAsync(token, "init", "--initial-branch=main").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "user.name", "Retro IDE Fixture").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "user.email", "fixture@example.invalid").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "commit.gpgsign", "false").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "core.autocrlf", "false").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "core.hooksPath", hooks).ConfigureAwait(false);
            if (head != FixtureHead.Unborn)
            {
                await File.WriteAllTextAsync(WorkingFile, "prior revision\n", token).ConfigureAwait(false);
                await GitAsync(token, "add", "--", Source).ConfigureAwait(false);
                await GitAsync(token, "commit", "-m", "Seed fixture").ConfigureAwait(false);
            }
            if (head == FixtureHead.Detached)
                await GitAsync(token, "switch", "--detach", "HEAD").ConfigureAwait(false);
            await File.WriteAllTextAsync(WorkingFile, Staged, token).ConfigureAwait(false);
            await GitAsync(token, "add", "--", Source).ConfigureAwait(false);
            await File.WriteAllTextAsync(WorkingFile, Dirty, token).ConfigureAwait(false);

            var opened = await MutateAsync("workspace_open_root", new(StringComparer.Ordinal)
                { ["path"] = checkout, ["kind"] = "folder" }, token).ConfigureAwait(false);
            Assert.Equal("ok", Code(opened));
            var selected = await MutateAsync("git_select_repository", new(StringComparer.Ordinal)
                { ["rootId"] = opened.GetProperty("data").GetProperty("id").GetGuid(), ["relativePath"] = "" }, token).ConfigureAwait(false);
            Assert.Equal("ok", Code(selected));
        }

        public async Task<CommitState> ReadStateAsync(CancellationToken token)
        {
            var head = await GitAsync(token, "rev-parse", "HEAD").ConfigureAwait(false);
            var parent = await GitAsync(token, "show", "-s", "--format=%P", "HEAD").ConfigureAwait(false);
            var committed = await GitAsync(token, "show", "HEAD:" + Source).ConfigureAwait(false);
            var staged = await GitAsync(token, "show", ":" + Source).ConfigureAwait(false);
            var working = await File.ReadAllTextAsync(WorkingFile, token).ConfigureAwait(false);
            return new(head, parent.Length == 0 ? null : parent, committed + '\n', staged + '\n', working);
        }

        internal async Task<CommitBoundary> ReadCommitBoundaryAsync(CancellationToken token)
        {
            const string HeadPrefix = "# branch.oid ";
            const string BranchPrefix = "# branch.head ";
            var status = await GitAsync(token, "status", "--porcelain=v2", "--branch", "--untracked-files=no").ConfigureAwait(false);
            var headers = status.Split('\n', StringSplitOptions.TrimEntries);
            var branch = headers.Single(line => line.StartsWith(BranchPrefix, StringComparison.Ordinal))[BranchPrefix.Length..];
            var head = headers.Single(line => line.StartsWith(HeadPrefix, StringComparison.Ordinal))[HeadPrefix.Length..];
            var main = await GitAsync(token, "for-each-ref", "--format=%(objectname)", "refs/heads/main").ConfigureAwait(false);
            var staged = await GitAsync(token, "show", ":" + Source).ConfigureAwait(false);
            var working = await File.ReadAllTextAsync(WorkingFile, token).ConfigureAwait(false);
            return new(branch, head == "(initial)" ? null : head, main, staged + '\n', working);
        }

        public async Task<string> ReviewAsync(CancellationToken token)
        {
            var reply = await CallAsync("git_get_commit_state", new(StringComparer.Ordinal)
                { ["worktreePath"] = checkout }, token).ConfigureAwait(false);
            Assert.Equal("ok", Code(reply));
            var preimage = reply.GetProperty("data").GetProperty("preimage").GetString();
            Assert.NotNull(preimage);
            return preimage;
        }

        public async Task ReplaceIndexTextAsync(string text, CancellationToken token)
        {
            var working = await File.ReadAllTextAsync(WorkingFile, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(WorkingFile, text, token).ConfigureAwait(false);
            try { await GitAsync(token, "add", "--", Source).ConfigureAwait(false); }
            finally { await File.WriteAllTextAsync(WorkingFile, working, CancellationToken.None).ConfigureAwait(false); }
        }

        public Task ConfigureHookAsync(bool allow, CancellationToken token) => WritePreCommitHookAsync(
            allow ? "#!/bin/sh\nexit 0\n" : "#!/bin/sh\nprintf 'fixture rejects commit\\n' >&2\nexit 1\n", token);

        internal Task ConfigureRestagingHookAsync(CancellationToken token) =>
            WritePreCommitHookAsync("#!/bin/sh\ngit add -- " + Source + "\n", token);

        private async Task WritePreCommitHookAsync(string script, CancellationToken token)
        {
            var path = Path.Combine(hooks, "pre-commit");
            await File.WriteAllTextAsync(path, script, token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public async Task<string?> CommitAsync(string preimage, CancellationToken token) =>
            Code(await MutateAsync("git_commit", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout, ["preimage"] = preimage, ["message"] = "Publish the reviewed fixture index",
            }, token).ConfigureAwait(false));

        private Task<string> GitAsync(CancellationToken token, params string[] args) => GitFixture.GitAsync(checkout, token, args);

        private async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> arguments, CancellationToken token)
        {
            var current = await CallAsync("workbench_get_context", null, token).ConfigureAwait(false);
            arguments["context"] = current.GetProperty("context");
            return await CallAsync(tool, arguments, token).ConfigureAwait(false);
        }

        private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?>? arguments, CancellationToken token)
        {
            var reply = await client.CallToolAsync(tool, arguments, cancellationToken: token).ConfigureAwait(false);
            Assert.NotNull(reply.StructuredContent);
            return reply.StructuredContent.Value;
        }

        private static string? Code(JsonElement result) => result.GetProperty("code").GetString();
    }
}
