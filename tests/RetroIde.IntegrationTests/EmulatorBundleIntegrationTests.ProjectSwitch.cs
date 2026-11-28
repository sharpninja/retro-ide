using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RetroIde.Application.Emulators;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Switching_projects_while_a_package_notification_reads_the_old_selection_updates_the_pane()
    {
        using var root = new BundleStoreFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(root.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = OpenEmulatorWorkbench(root);
        await using var http = await Connection.ConnectAsync(session, token);
        await http.ChangeAsync("emulator_configure_bucket", BucketArguments(new Uri(git.Remote).AbsoluteUri));
        var first = await InstallAsync(FirstPin);
        var next = await InstallAsync(NextPin);
        var alpha = await OpenProjectAsync(http, root.Directory, "alpha");
        var beta = await OpenProjectAsync(http, root.Directory, "beta");
        await http.ChangeAsync("emulator_select_bundle", ProjectArguments(alpha, first));
        await http.ChangeAsync("emulator_select_bundle", ProjectArguments(beta, next));
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            session.View.FocusTarget = "emulators";
            await session.View.FocusCommand.ExecuteAsync(null);
            await ClickEmulatorButtonAsync(window, "emulator.buckets.list.toolbar", token);
            var view = session.EmulatorsView;
            while (view.IsBusy || view.Projects.Length != 2)
                await Task.Delay(25, token);
            var picker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), control =>
                string.Equals(AutomationProperties.GetAutomationId(control), "emulator.projects.items", StringComparison.Ordinal));
            picker.SelectedItem = Assert.Single(view.Projects, project => project.Id == alpha);
            while (view.IsBusy || view.ActivePin != first)
                await Task.Delay(25, token);
            await VerifyProjectSwitchAsync(view, picker, http, alpha, beta, next, token);
        }
        finally { window.Close(); }

        async Task<Pin> InstallAsync(Pin pin)
        {
            var release = await PublishPackageAsync(root, git, pin);
            await http.ChangeAsync("emulator_refresh_bucket", BucketArguments());
            await http.ChangeAsync("emulator_install_bundle", PackageArguments(release));
            return release;
        }
    }

    private static async Task<Guid> OpenProjectAsync(Connection http, string parent, string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(parent, name)).FullName.Replace('\\', '/');
        var result = await http.ChangeAsync("workspace_open_root", new(StringComparer.Ordinal) { ["kind"] = "project", ["path"] = path });
        return result.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task VerifyProjectSwitchAsync(EmulatorsViewModel view, ComboBox picker, Connection http, Guid alpha, Guid beta,
        Pin expected, CancellationToken token)
    {
        var intercepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CommandRequested += PauseSelection;
        try
        {
            await http.ChangeAsync("emulator_refresh_bucket", BucketArguments());
            await intercepted.Task.WaitAsync(token);
            Assert.True(view.IsBusy);
            Assert.True(picker.IsEffectivelyEnabled);
            picker.SelectedItem = Assert.Single(view.Projects, project => project.Id == beta);
            Assert.Equal(beta, view.SelectedProject?.Id);
            Assert.Null(view.ActivePin);
            Assert.Null(view.ActiveBinding);
            Assert.Empty(view.PreviousPins);
            release.SetResult();
            while (view.IsBusy)
                await Task.Delay(25, token);

            Assert.Equal(expected, view.ActivePin);
            Assert.Equal("ready", view.ActiveBinding?.Status, StringComparer.Ordinal);
            Assert.Equal("amiga-aga", Assert.Single(view.ActiveBinding!.VerifiedDebuggerProfiles!).Id, StringComparer.Ordinal);
            Assert.Empty(view.PreviousPins);
        }
        finally
        {
            release.TrySetResult();
            view.CommandRequested -= PauseSelection;
        }

        void PauseSelection(object? sender, ViewCommandRequested request)
        {
            if (request.Payload is EmulatorFeatures.ProjectInput project && project.RootId == alpha &&
                request.Completion is { } completion && intercepted.TrySetResult())
                request.Completion = HoldAsync(completion);
        }

        async Task HoldAsync(Task completion)
        {
            await completion;
            await release.Task.WaitAsync(token);
        }
    }
}
