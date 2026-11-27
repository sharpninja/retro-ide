using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TEST-RETRO-009#ac-test-bucket-platforms; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Docked_emulators_refreshes_installs_and_selects_a_release_from_an_MCP_bucket()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var pin = await PublishPackageAsync(root, git, FirstPin);
        await using var session = OpenEmulatorWorkbench(root);
        await using var http = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await http.ChangeAsync("emulator_configure_bucket", BucketArguments(new Uri(git.Remote).AbsoluteUri));
        var project = Directory.CreateDirectory(Path.Combine(root.Directory, "pane project"));
        var rootId = (await http.ChangeAsync("workspace_open_root", new(StringComparer.Ordinal)
        {
            ["kind"] = "project", ["path"] = project.FullName.Replace('\\', '/'),
        })).GetProperty("data").GetProperty("id").GetGuid();
        var window = new WorkbenchWindow(session);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(40));
            session.View.FocusTarget = "emulators";
            Assert.True(session.View.FocusCommand.CanExecute(null));
            await session.View.FocusCommand.ExecuteAsync(null);
            Assert.Equal("emulators", session.Layout.ActiveToolId, StringComparer.Ordinal);
            await ClickEmulatorButtonAsync(window, "emulator.buckets.list.toolbar", deadline.Token);
            await ClickEmulatorButtonAsync(window, "emulator.bucket.refresh.toolbar", deadline.Token);
            while (!string.Equals(await CachedRevisionAsync(http), pin.ManifestRevision, StringComparison.Ordinal))
                await Task.Delay(25, deadline.Token);
            await AssertCatalogAsync(http, pin.ManifestRevision);
            await ClickEmulatorButtonAsync(window, "emulator.bundle.install.toolbar", deadline.Token);
            await ClickEmulatorButtonAsync(window, "emulator.project.select.toolbar", deadline.Token);
            while (await SelectedPinAsync(http, rootId) != pin)
                await Task.Delay(25, deadline.Token);
            var picker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), control =>
                string.Equals(AutomationProperties.GetAutomationId(control), "emulator.projects.items", StringComparison.Ordinal));
            Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text =>
                string.Equals(text.Text, project.FullName.Replace('\\', '/'), StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(token);
        }
    }

    /// <summary>Checks that the desktop closing path releases a stalled native Git operation.</summary>
    [AvaloniaFact]
    public async Task Closing_the_workbench_drains_an_active_bucket_refresh_before_the_window_closes()
    {
        using var root = new BundleStoreFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(root.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var session = OpenEmulatorWorkbench(root);
        await using var http = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await http.ChangeAsync("emulator_configure_bucket", BucketArguments($"http://127.0.0.1:{endpoint.Port}/bucket.git"));
        var window = new WorkbenchWindow(session);
        var blockedAtClose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => blockedAtClose.TrySetResult(session.EmulatorsView.IsBusy);
        window.Show();
        try
        {
            session.View.FocusTarget = "emulators";
            await session.View.FocusCommand.ExecuteAsync(null);
            await ClickEmulatorButtonAsync(window, "emulator.buckets.list.toolbar", token);
            await ClickEmulatorButtonAsync(window, "emulator.bucket.refresh.toolbar", token);
            using var connection = await listener.AcceptTcpClientAsync(token);
            Assert.True(session.Interaction.IsBlocked("emulators"));

            window.Close();

            Assert.False(await blockedAtClose.Task.WaitAsync(token));
            Assert.False(session.Interaction.IsBlocked("emulators"));
        }
        finally
        {
            listener.Stop();
            if (window.IsVisible)
                window.Close();
        }
    }

    private static async Task ClickEmulatorButtonAsync(WorkbenchWindow window, string id, CancellationToken token)
    {
        while (true)
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEnabled &&
                string.Equals(AutomationProperties.GetAutomationId(button), id, StringComparison.Ordinal) &&
                button.Command?.CanExecute(button.CommandParameter) == true);
            if (button is not null)
            {
                button.BringIntoView();
                await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
                return;
            }
            await Task.Delay(25, token).ConfigureAwait(true);
        }
    }

    private static async Task<string?> CachedRevisionAsync(GitWorkspaceStateTests.WorkspaceConnection http)
    {
        var result = await http.CallAsync("emulator_list_buckets").ConfigureAwait(true);
        var row = Assert.Single(result.GetProperty("data").GetProperty("buckets").EnumerateArray());
        return row.TryGetProperty("revision", out JsonElement value) ? value.GetString() : null;
    }

    /// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2.</remarks>
    [AvaloniaFact]
    public void A_saved_layout_gains_the_emulators_pane_and_keeps_its_existing_topology()
    {
        var id = Guid.NewGuid();
        using var original = new WorkbenchSession(id);
        Assert.True(original.Layout.Focus("output"));
        var current = original.Layout.Capture();
        var older = current with { Main = current.Main with { Content = RemoveEmulators(current.Main.Content) } };

        using var reopened = new WorkbenchSession(id, older);
        var restored = reopened.Layout.Capture();
        Assert.Equal("output", restored.ActiveTool, StringComparer.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(older.Main.Content), JsonSerializer.Serialize(restored.Main.Content), StringComparer.Ordinal);
        Assert.Contains(restored.Main.Pinned, pin => string.Equals(pin.Tool, "emulators", StringComparison.Ordinal));
    }

    private static DockNode RemoveEmulators(DockNode node) => node.Children is not { } children ? node :
        node with { Children = children.Where(child => !string.Equals(child.Id, "emulators", StringComparison.Ordinal))
            .Select(RemoveEmulators).ToArray() };
}
