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

    /// <summary>Verifies the Git menu command remains available if Source Control reopens in a floating host.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. The main workbench owns the command before Dock materializes the floating window.</remarks>
    [AvaloniaFact]
    public async Task Git_menu_can_focus_Source_Control_restored_in_a_floating_host()
    {
        var legacy = LegacyLayout();
        var floating = Assert.Single(legacy.Floating);
        var restoredGroup = floating.Content with { Children = [.. floating.Content.Children!, new("tool", Pane)] };
        using var session = new WorkbenchSession(Guid.NewGuid(), legacy with
        {
            ActiveTool = null,
            Floating = [floating with { Content = restoredGroup }],
        });
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Single(session.Layout.Root.Windows!);
            var menu = Find<MenuItem>(window, "menu.git");
            var actions = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource);
            var focus = Assert.Single(actions, action => HasId(action, "git.source-control.focus.menu"));
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(focus.Command);
            Assert.True(command.CanExecute(null), "The Git menu cannot focus a restored floating Source Control pane.");
            await command.ExecuteAsync(null);
            Assert.Equal("ok", session.SourceControl.LastResult?.Code);
            var pane = session.Layout.Tool(Pane);
            var group = Assert.IsAssignableFrom<Dock.Model.Core.IDock>(pane.Owner);
            var root = Assert.IsAssignableFrom<Dock.Model.Controls.IRootDock>(session.Layout.Factory.FindRoot(pane));
            Assert.Same(pane, group.ActiveDockable);
            Assert.Same(pane, root.FocusedDockable);
        }
        finally { window.Close(); }
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
            var worktree = await AddRootAsync(sample.Primary).ConfigureAwait(true);
            _linkedRoot = await AddRootAsync(sample.Linked).ConfigureAwait(true);
            await WriteAsync("workspace_select_entry", Address(worktree)).ConfigureAwait(true);
            await ClickAsync("git.repository.select.pane").ConfigureAwait(true);
            Assert.Equal(sample.Primary, await ReadMcpSelectionAsync().ConfigureAwait(true), Paths);
            await ClickAsync("git.status.read.pane").ConfigureAwait(true);
        }

        public async Task SelectOverMcpAsync()
        {
            await WriteAsync("git_select_repository", Address(_linkedRoot)).ConfigureAwait(false);
            await ReadMcpSelectionAsync().ConfigureAwait(false);
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
            var status = await InvokeAsync("git_get_status").ConfigureAwait(false);
            var path = status.GetProperty("data").GetProperty("repository").GetProperty("worktreePath").GetString();
            return Assert.IsType<string>(path);
        }

        private async Task ClickAsync(string id)
        {
            window.UpdateLayout();
            var button = Find<Button>(window, id);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
            var previous = command.ExecutionTask;
            await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
            var execution = Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.NotSame(previous, execution);
            await execution.WaitAsync(token).ConfigureAwait(false);
        }

        private string[] ReadPaths(string id) => Assert.IsAssignableFrom<IEnumerable<GitStatusEntry>>(Find<ItemsControl>(window, id).ItemsSource)
            .Select(entry => entry.Path).ToArray();

        private async Task<Guid> AddRootAsync(string path)
        {
            var result = await WriteAsync("workspace_open_root", new(StringComparer.Ordinal) { ["path"] = path, ["kind"] = "folder" }).ConfigureAwait(false);
            return result.GetProperty("data").GetProperty("id").GetGuid();
        }

        private async Task<JsonElement> WriteAsync(string name, Dictionary<string, object?> args)
        {
            args.Add("context", (await InvokeAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context"));
            return await InvokeAsync(name, args).ConfigureAwait(false);
        }

        private async Task<JsonElement> InvokeAsync(string name, IReadOnlyDictionary<string, object?>? args = null)
        {
            var response = await client.CallToolAsync(name, args, cancellationToken: token).ConfigureAwait(false);
            Assert.NotNull(response.StructuredContent);
            var result = response.StructuredContent.Value;
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        private static Dictionary<string, object?> Address(Guid root) =>
            new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" };
    }
}
