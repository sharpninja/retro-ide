using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02ExplorerRoutingContractTests
{
    /// <summary>Verifies mixed-root routing through an official SDK client attached to the live workbench.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots. Use the same checks as the reference fixture.</remarks>
    [AvaloniaFact]
    public async Task Routes_mixed_roots_and_same_name_documents_through_the_workbench()
    {
        using var inputs = new ExplorerInputs();
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
        var attachment = host.CreateAttachment("explorer-routing-contract", canMutate: true);
        using var http = attachment.CreateHttpClient();
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = attachment.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http), cancellationToken: token);
        var tools = (await client.ListToolsAsync(cancellationToken: token)).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        await VerifyMixedRootsAsync(new WorkbenchExplorerPort(client, tools, session.View.WorkspaceId, token), inputs);
    }

    private sealed class WorkbenchExplorerPort(McpClient client, IReadOnlySet<string> tools, Guid workspaceId, CancellationToken token) : IExplorerPort
    {
        public async Task<IReadOnlyList<RootInfo>> ReadRootsAsync() =>
            (await InvokeAsync("workspace_list_roots")).GetProperty("data").EnumerateArray().Select(ReadRoot).ToArray();

        public async Task<RootInfo> OpenRootAsync(string path, string kind) =>
            ReadRoot(await MutateAsync("workspace_open_root", new(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["kind"] = kind,
            }));

        public async Task<BranchInfo> ReadBranchAsync(Guid rootId)
        {
            var outcome = await InvokeAsync("workspace_list_children", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rootId"] = rootId,
                ["relativePath"] = "",
            });
            var data = outcome.GetProperty("data");
            Assert.Equal(workspaceId, data.GetProperty("workspaceId").GetGuid());
            var parent = data.GetProperty("parent");
            Assert.Equal("", parent.GetProperty("relativePath").GetString());
            var entries = data.GetProperty("entries").EnumerateArray().ToArray();
            Assert.All(entries, entry =>
            {
                Assert.False(entry.GetProperty("isDirectory").GetBoolean());
                Assert.Equal(rootId, entry.GetProperty("address").GetProperty("rootId").GetGuid());
            });
            return new BranchInfo(parent.GetProperty("rootId").GetGuid(), data.GetProperty("isAvailable").GetBoolean(),
                entries.Select(entry => ReadString(entry, "name")).ToArray());
        }

        public async Task<DocumentInfo> OpenDocumentAsync(Guid rootId, string relativePath)
        {
            var data = await MutateAsync("workspace_open_file", new(StringComparer.Ordinal)
            {
                ["rootId"] = rootId,
                ["relativePath"] = relativePath,
            });
            var viewKey = data.GetProperty("viewKey").GetGuid();
            var current = (await InvokeAsync("editor_get_document", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["viewKey"] = viewKey,
            })).GetProperty("data");
            Assert.Equal(workspaceId, data.GetProperty("workspaceId").GetGuid());
            Assert.Equal(workspaceId, current.GetProperty("workspaceId").GetGuid());
            Assert.Equal(ReadDocument(data), ReadDocument(current));
            return ReadDocument(data);
        }

        private async Task<JsonElement> MutateAsync(string name, Dictionary<string, object?> arguments)
        {
            arguments.Add("context", (await InvokeAsync("workbench_get_context")).GetProperty("context"));
            return (await InvokeAsync(name, arguments)).GetProperty("data");
        }

        private async Task<JsonElement> InvokeAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            Assert.True(tools.Contains(name), $"The live workbench did not advertise {name}.");
            var result = await client.CallToolAsync(name, arguments, cancellationToken: token);
            Assert.NotNull(result.StructuredContent);
            var outcome = result.StructuredContent.Value;
            Assert.Equal("ok", outcome.GetProperty("code").GetString());
            return outcome;
        }

        private static RootInfo ReadRoot(JsonElement data) =>
            new(data.GetProperty("id").GetGuid(), ReadString(data, "kind").ToLowerInvariant(), ReadString(data, "path"));

        private static DocumentInfo ReadDocument(JsonElement data) =>
            new(data.GetProperty("viewKey").GetGuid(), data.GetProperty("documentId").GetGuid(),
                data.GetProperty("rootId").GetGuid(), ReadString(data, "relativePath"));

        private static string ReadString(JsonElement data, string field) => Assert.IsType<string>(data.GetProperty(field).GetString());
    }
}
