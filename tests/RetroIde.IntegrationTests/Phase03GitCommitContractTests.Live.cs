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
    public async Task Live_workbench_publishes_only_the_reviewed_index()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var folder = Directory.CreateTempSubdirectory("retro-commit-contract-");
        try
        {
            await VerifyWorkbenchAsync(folder, timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => file.IsReadOnly))
                    file.IsReadOnly = false;
            folder.Delete(recursive: true);
        }
    }

    private static async Task VerifyWorkbenchAsync(DirectoryInfo folder, CancellationToken token)
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
            await port.ConfigureFixtureAsync(token).ConfigureAwait(true);
            await VerifyReviewedCommitAsync(port, token).ConfigureAwait(true);
        }
        finally { await GitFixture.DisposeWorkbenchAsync(session).ConfigureAwait(true); }
    }

    private sealed class LiveCommitPort(McpClient client, string checkout, string hooks) : IReviewedCommitPort
    {
        private const string Source = "contract.asm";
        private string WorkingFile => Path.Combine(checkout, Source);

        internal async Task ConfigureFixtureAsync(CancellationToken token)
        {
            await GitAsync(token, "init", "--initial-branch=main").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "user.name", "Retro IDE Fixture").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "user.email", "fixture@example.invalid").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "commit.gpgsign", "false").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "core.autocrlf", "false").ConfigureAwait(false);
            await GitAsync(token, "config", "--local", "core.hooksPath", hooks).ConfigureAwait(false);
            await File.WriteAllTextAsync(WorkingFile, "prior revision\n", token).ConfigureAwait(false);
            await GitAsync(token, "add", "--", Source).ConfigureAwait(false);
            await GitAsync(token, "commit", "-m", "Seed fixture").ConfigureAwait(false);
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

        public async Task ConfigureHookAsync(bool allow, CancellationToken token)
        {
            var path = Path.Combine(hooks, "pre-commit");
            var script = allow ? "#!/bin/sh\nexit 0\n" : "#!/bin/sh\nprintf 'fixture rejects commit\\n' >&2\nexit 1\n";
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
