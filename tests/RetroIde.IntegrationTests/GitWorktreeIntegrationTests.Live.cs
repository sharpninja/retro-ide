using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitWorktreeIntegrationTests
{
    /// <summary>Runs the shared Git assertions through direct HTTP and stdio attachments to one workbench.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Verify both transports observe the same selected worktree state.</remarks>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Read the optional original path from the workbench's wire projection.</remarks>
    [AvaloniaFact]
    public async Task Live_discovery_and_status_use_the_selected_workspace_worktree()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var sample = await CreateSampleAsync(token);
        using var session = new WorkbenchSession(Guid.NewGuid());
        var scratch = Directory.CreateTempSubdirectory("retro-ide-git-context-");
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var attachment = host.CreateAttachment("git-context-editor", canMutate: true);
            var attachmentPath = Path.Combine(scratch.FullName, "attachment.json");
            attachment.Write(attachmentPath);
            using var http = attachment.CreateHttpClient();
            await using var direct = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token);
            await using var bridge = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = "dotnet",
                Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, attachmentPath],
            }), cancellationToken: token);
            var directTools = await direct.ListToolsAsync(cancellationToken: token);
            var bridgeTools = await bridge.ListToolsAsync(cancellationToken: token);
            foreach (var name in new[] { "git_discover_repository", "git_select_repository", "git_get_status" })
            {
                Assert.True(directTools.Any(tool => string.Equals(tool.Name, name, StringComparison.Ordinal)),
                    $"The live workbench did not advertise {name}.");
                var directTool = Assert.Single(directTools, tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
                var bridgeTool = Assert.Single(bridgeTools, tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
                Assert.Equal(directTool.JsonSchema.GetRawText(), bridgeTool.JsonSchema.GetRawText());
                Assert.Equal(directTool.ProtocolTool.OutputSchema?.GetRawText(), bridgeTool.ProtocolTool.OutputSchema?.GetRawText());
            }
            await VerifyWorktreesAsync(new LiveGitPort(direct, bridge, token), sample);
        }
        finally { scratch.Delete(recursive: true); }
    }

    private sealed class LiveGitPort(McpClient direct, McpClient bridge, CancellationToken token) : IGitPort
    {
        public async Task<Guid> OpenRootAsync(string path)
        {
            var opened = await InvokeAsync(direct, "workspace_open_root", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["kind"] = "folder",
                ["context"] = await ContextAsync(direct),
            });
            return Data(opened).GetProperty("id").GetGuid();
        }

        public async Task<GitRepository> DiscoverAsync(Guid root, string relativePath) =>
            ReadRepository(Data(await InvokeAsync(direct, "git_discover_repository", Address(root, relativePath))));

        public async Task<string> DiscoveryCodeAsync(Guid root, string relativePath) =>
            Code(await InvokeAsync(direct, "git_discover_repository", Address(root, relativePath)));

        public async Task SelectAsync(Guid root, string relativePath)
        {
            var arguments = Address(root, relativePath);
            arguments.Add("context", await ContextAsync(bridge));
            var selected = await InvokeAsync(bridge, "git_select_repository", arguments);
            Assert.Equal("ok", Code(selected));
            Assert.Equal("stale_revision", Code(await InvokeAsync(direct, "git_select_repository", arguments)));
        }

        public async Task<string> StatusCodeAsync() => Code(await InvokeAsync(direct, "git_get_status"));

        public async Task<GitView> StatusAsync()
        {
            var data = Data(await InvokeAsync(direct, "git_get_status"));
            var relayed = Data(await InvokeAsync(bridge, "git_get_status"));
            Assert.Equal(data.GetRawText(), relayed.GetRawText());
            return new GitView(ReadRepository(data.GetProperty("repository")), Text(data, "branch"),
                data.GetProperty("entries").EnumerateArray().Select(entry => new GitEntry(Text(entry, "path"),
                    entry.TryGetProperty("originalPath", out var original) ? original.GetString() : null,
                    Text(entry, "index"), Text(entry, "worktree"))).ToArray());
        }

        private async Task<JsonElement> ContextAsync(McpClient client) => (await InvokeAsync(client, "workbench_get_context")).GetProperty("context");

        private async Task<JsonElement> InvokeAsync(McpClient client, string name, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            var result = await client.CallToolAsync(name, arguments, cancellationToken: token);
            Assert.NotNull(result.StructuredContent);
            return result.StructuredContent.Value;
        }

        private static Dictionary<string, object?> Address(Guid root, string relativePath) =>
            new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = relativePath };

        private static GitRepository ReadRepository(JsonElement value) =>
            new(Text(value, "worktreePath"), Text(value, "gitDirectory"), Text(value, "commonDirectory"));

        private static JsonElement Data(JsonElement value)
        {
            Assert.Equal("ok", Code(value));
            return value.GetProperty("data");
        }

        private static string Code(JsonElement value) => Text(value, "code");

        private static string Text(JsonElement value, string key) => Assert.IsType<string>(value.GetProperty(key).GetString());
    }
}
