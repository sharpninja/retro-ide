using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitRepositorySetupTests
{
    /// <summary>Proves native repository setup through both official MCP transports and their permission boundaries.</summary>
    [AvaloniaFact]
    public async Task Mcp_setup_creates_scoped_worktrees_and_rejects_unauthorized_changes()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var scratch = Directory.CreateTempSubdirectory("retro-ide-setup-mcp-");
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var attachment = host.CreateAttachment("git-setup", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var direct = await ConnectAsync(attachment.Endpoint, http, token);
            var file = Path.Combine(scratch.FullName, "attachment.json");
            attachment.Write(file);
            await using var bridge = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = "dotnet",
                Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, file],
            }), cancellationToken: token);
            var available = await direct.ListToolsAsync(cancellationToken: token);
            var relayed = await bridge.ListToolsAsync(cancellationToken: token);
            foreach (var name in new[] { "git_initialize_repository", "git_clone_repository" })
            {
                Assert.True(available.Any(tool => string.Equals(name, tool.Name, StringComparison.Ordinal)),
                    $"The workbench does not advertise {name}.");
                Assert.Equal(Assert.Single(available, tool => string.Equals(name, tool.Name, StringComparison.Ordinal)).JsonSchema.GetRawText(),
                    Assert.Single(relayed, tool => string.Equals(name, tool.Name, StringComparison.Ordinal)).JsonSchema.GetRawText());
            }

            var oldContext = (await InvokeAsync(direct, "workbench_get_context", token)).GetProperty("context");
            await VerifyProvisionAsync(new McpSetupPort(direct, bridge, token), sample.Remote,
                () => session.Controller.DrainGitRefreshAsync(session.View));
            var protectedPath = Path.Combine(scratch.FullName, "protected checkout");
            var current = (await InvokeAsync(direct, "workbench_get_context", token)).GetProperty("context");
            var stale = await InvokeAsync(direct, "git_initialize_repository", token,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["destination"] = protectedPath, ["context"] = oldContext });
            Assert.Equal("stale_revision", Code(stale));
            var observation = host.CreateAttachment("git-setup-observer", canMutate: false);
            using var observerHttp = observation.CreateHttpClient();
            await using var observer = await ConnectAsync(observation.Endpoint, observerHttp, token);
            var denied = await InvokeAsync(observer, "git_clone_repository", token,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["source"] = sample.Remote, ["destination"] = protectedPath, ["context"] = current,
                });
            Assert.Equal("forbidden", Code(denied));
            Assert.False(Path.Exists(protectedPath));
        }
        finally { scratch.Delete(recursive: true); }
    }

    /// <summary>Uses the rendered Git menu and the repository dialog to initialize and clone into the desktop workspace.</summary>
    [AvaloniaFact]
    public async Task Desktop_setup_routes_Git_menu_commands_to_the_workspace()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var scratch = Directory.CreateTempSubdirectory("retro-ide-setup-view-");
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            window.UpdateLayout();
            var menu = Assert.Single(window.GetVisualDescendants().OfType<MenuItem>(), item => HasId(item, "menu.git"));
            var entries = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource);
            await SubmitDialogAsync("git.repository.initialize.menu", null, "new development root");
            await SubmitDialogAsync("git.repository.clone.menu", sample.Remote, "cloned development root");

            async Task SubmitDialogAsync(string id, string? source, string name)
            {
                var menuItem = entries.SingleOrDefault(item => HasId(item, id));
                Assert.True(menuItem is not null, $"The Git menu does not contain {id}.");
                Assert.NotNull(menuItem.Icon);
                Assert.NotNull(menuItem.InputGesture);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(menuItem.Command);
                var completion = command.ExecuteAsync(null);
                while (window.OwnedWindows.Count == 0)
                    await Task.Delay(15, token).ConfigureAwait(true);
                var dialog = Assert.Single(window.OwnedWindows);
                dialog.UpdateLayout();
                var destination = Path.Combine(scratch.FullName, name);
                Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), input => HasId(input, "git.setup.destination")).Text = destination;
                if (source is not null)
                    Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), input => HasId(input, "git.setup.source")).Text = source;
                await WorkbenchRemoteControl.ClickAsync(dialog, "git.setup.submit").ConfigureAwait(true);
                await completion.WaitAsync(token).ConfigureAwait(true);
                Assert.Equal("ok", session.SourceControl.LastResult?.Code);
                Assert.True(string.Equals("source-control", session.View.ActiveToolId, StringComparison.Ordinal),
                    "The setup workflow did not surface Source Control feedback.");
                Assert.Equal(destination, session.SourceControl.Repository?.WorktreePath, NativePath);
                Assert.Contains(session.Explorer.Roots, root => NativePath.Equals(destination, root.Path));
                Assert.True(Directory.Exists(Path.Combine(destination, ".git")));
            }
        }
        finally
        {
            window.Close();
            await session.Controller.DrainGitRefreshAsync(session.View);
            GitWorktreeFixture.DeleteOwnedDirectory(scratch);
        }
    }

    private static bool HasId(Control control, string id) =>
        string.Equals(id, AutomationProperties.GetAutomationId(control), StringComparison.Ordinal);

    private static Task<McpClient> ConnectAsync(Uri endpoint, HttpClient http, CancellationToken token) =>
        McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http), cancellationToken: token);

    private static async Task<JsonElement> InvokeAsync(McpClient client, string name, CancellationToken token,
        IReadOnlyDictionary<string, object?>? args = null)
    {
        var response = await client.CallToolAsync(name, args, cancellationToken: token).ConfigureAwait(false);
        Assert.NotNull(response.StructuredContent);
        return response.StructuredContent.Value;
    }

    private static string Code(JsonElement result) => Assert.IsType<string>(result.GetProperty("code").GetString());

    private sealed class McpSetupPort(McpClient direct, McpClient bridge, CancellationToken token) : IProvisionPort
    {
        public Task<string> InitializeAsync(string destination) => MutateAsync(direct, "git_initialize_repository",
            new(StringComparer.Ordinal) { ["destination"] = destination });

        public Task<string> CloneAsync(string source, string destination) => MutateAsync(bridge, "git_clone_repository",
            new(StringComparer.Ordinal) { ["source"] = source, ["destination"] = destination });

        public async Task<string[]> RootsAsync()
        {
            var result = await InvokeAsync(direct, "workspace_list_roots", token).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data").EnumerateArray()
                .Select(root => Assert.IsType<string>(root.GetProperty("path").GetString())).ToArray();
        }

        public async Task<ProvisionStatus> StatusAsync()
        {
            var first = await InvokeAsync(direct, "git_get_status", token).ConfigureAwait(false);
            var second = await InvokeAsync(bridge, "git_get_status", token).ConfigureAwait(false);
            Assert.Equal("ok", Code(first));
            Assert.Equal("ok", Code(second));
            var status = first.GetProperty("data");
            Assert.Equal(status.GetRawText(), second.GetProperty("data").GetRawText());
            return new(Assert.IsType<string>(status.GetProperty("repository").GetProperty("worktreePath").GetString()),
                status.TryGetProperty("head", out var oid) ? oid.GetString() : null);
        }

        private async Task<string> MutateAsync(McpClient client, string name, Dictionary<string, object?> args)
        {
            var live = await InvokeAsync(client, "workbench_get_context", token).ConfigureAwait(false);
            args.Add("context", live.GetProperty("context"));
            return Code(await InvokeAsync(client, name, token, args).ConfigureAwait(false));
        }
    }
}
