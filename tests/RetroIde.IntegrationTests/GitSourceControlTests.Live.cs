using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;
using RetroIde.Application.Git;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitSourceControlTests
{
    /// <summary>Proves the real Git menu and pane drive the same registered worktree as an MCP client.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Use RemoteControl on the toolbar actions and verify actual visible status.</remarks>
    [AvaloniaFact]
    public async Task Workbench_Git_menu_and_pane_share_worktree_state_with_mcp()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = deadline.Token;
        using var sample = await CreateSourceControlSampleAsync(token);
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1280, Height = 900 };
        window.Show();
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var attachment = host.CreateAttachment("git-source-control", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token);
            window.UpdateLayout();
            var menu = Find<MenuItem>(window, "menu.git");
            var items = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource);
            foreach (var item in items)
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)));
                Assert.NotNull(item.Icon);
                Assert.NotNull(item.InputGesture);
            }

            var focus = Assert.Single(items, item => HasId(item, "git.source-control.focus.menu"));
            var focusCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(focus.Command);
            await focusCommand.ExecuteAsync(null).WaitAsync(token);
            Assert.True(session.Layout.IsFocused(Pane));
            await VerifySurfaceAsync(new LiveSurface(window, client, sample, token), sample);
        }
        finally { window.Close(); }
    }

    /// <summary>Proves a saved layout with the old pane set retains its existing Dock topology and selection.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-001#verify-ac-behavior-desktop-2. Reload the migrated snapshot to ensure migration is idempotent.</remarks>
    [AvaloniaFact]
    public void Workbench_migrates_saved_layout_to_include_Source_Control()
    {
        using var first = new WorkbenchSession(Guid.NewGuid(), LegacyLayout());
        var migrated = first.Layout.Capture();
        VerifyMigration(migrated);
        Assert.Contains(first.Layout.Tool(Pane), first.Layout.Root.LeftPinnedDockables!);
        using var reopened = new WorkbenchSession(Guid.NewGuid(), migrated);
        VerifyMigration(reopened.Layout.Capture());
    }

    private static T Find<T>(Window window, string id) where T : Control
    {
        var found = window.GetVisualDescendants().OfType<T>().SingleOrDefault(item => HasId(item, id));
        Assert.True(found is not null, $"The live workbench does not render {id}.");
        return found;
    }

    private static bool HasId(Control item, string id) => string.Equals(AutomationProperties.GetAutomationId(item), id, StringComparison.Ordinal);

    private sealed class LiveSurface(Window window, McpClient client, GitWorktreeFixture sample, CancellationToken token) : ISourceControlSurface
    {
        private Guid _linkedRoot;

        public async Task SelectFromExplorerAsync()
        {
            var worktree = await AddRootAsync(sample.Primary);
            _linkedRoot = await AddRootAsync(sample.Linked);
            await WriteAsync("workspace_select_entry", Address(worktree));
            await ClickAsync("git.repository.select.pane");
            Assert.Equal(sample.Primary, await ReadMcpSelectionAsync(), Paths);
            await ClickAsync("git.status.read.pane");
        }

        public async Task SelectOverMcpAsync()
        {
            await WriteAsync("git_select_repository", Address(_linkedRoot));
            await ReadMcpSelectionAsync();
        }

        public Task<PaneSnapshot> ReadAsync()
        {
            window.UpdateLayout();
            return Task.FromResult(new PaneSnapshot(
                Assert.IsType<string>(Find<TextBlock>(window, "git.selection.path").Text),
                Assert.IsType<string>(Find<TextBlock>(window, "git.selection.branch").Text),
                ReadPaths("git.status.staged"), ReadPaths("git.status.working")));
        }

        public async Task<string> ReadMcpSelectionAsync()
        {
            var status = await InvokeAsync("git_get_status");
            var path = status.GetProperty("data").GetProperty("repository").GetProperty("worktreePath").GetString();
            return Assert.IsType<string>(path);
        }

        private async Task ClickAsync(string id)
        {
            window.UpdateLayout();
            var button = Find<Button>(window, id);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
            var previous = command.ExecutionTask;
            await WorkbenchRemoteControl.ClickAsync(window, id);
            var execution = Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.NotSame(previous, execution);
            await execution.WaitAsync(token);
        }

        private string[] ReadPaths(string id) => Assert.IsAssignableFrom<IEnumerable<GitStatusEntry>>(Find<ItemsControl>(window, id).ItemsSource)
            .Select(entry => entry.Path).ToArray();

        private async Task<Guid> AddRootAsync(string path)
        {
            var result = await WriteAsync("workspace_open_root", new(StringComparer.Ordinal) { ["path"] = path, ["kind"] = "folder" });
            return result.GetProperty("data").GetProperty("id").GetGuid();
        }

        private async Task<JsonElement> WriteAsync(string name, Dictionary<string, object?> args)
        {
            args.Add("context", (await InvokeAsync("workbench_get_context")).GetProperty("context"));
            return await InvokeAsync(name, args);
        }

        private async Task<JsonElement> InvokeAsync(string name, IReadOnlyDictionary<string, object?>? args = null)
        {
            var response = await client.CallToolAsync(name, args, cancellationToken: token);
            Assert.NotNull(response.StructuredContent);
            var result = response.StructuredContent.Value;
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        private static Dictionary<string, object?> Address(Guid root) =>
            new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" };
    }
}
