using Avalonia.Controls;
using Avalonia.RemoteControl.Server;
using Avalonia.RemoteControl.Server.Commands;
using Avalonia.RemoteControl.Server.Snapshots;
using Avalonia.RemoteControl.Server.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace RetroIde.Testing;

internal static class WorkbenchRemoteControl
{
    public static async Task ClickAsync(Window window, string automationId)
    {
        window.UpdateLayout();
        var options = Options.Create(new AvaloniaRemoteControlOptions { AllowRemoteActions = true });
        var dispatcher = new AvaloniaUiThreadRemoteControlDispatcher();
        var tree = new AvaloniaControlTreeSnapshotProvider(options, dispatcher);
        var snapshot = await tree.CaptureSnapshotAsync(window).ConfigureAwait(false);
        var control = snapshot.Nodes.Single(node => string.Equals(node.AutomationId, automationId, StringComparison.Ordinal));
        if (!control.IsVisible || !control.IsEnabled)
            throw new InvalidOperationException($"RemoteControl target {automationId} is unavailable.");

        var actions = new RemoteControlActionInvoker(
            tree, options, dispatcher, NullLogger<RemoteControlActionInvoker>.Instance);
        var result = await actions.InvokeClickAsync(control.Id, "retro-ide-tests").ConfigureAwait(false);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Message);
    }
}
