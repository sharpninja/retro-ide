using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.RemoteControl.Server;
using Avalonia.RemoteControl.Server.Rendering;
using Avalonia.RemoteControl.Server.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlBrandingTests
{
    internal static async Task RunAsync(CancellationTokenSource finished)
    {
        try { await VerifyAsync().ConfigureAwait(true); }
        finally { finished.Cancel(); }
    }

    internal static async Task VerifyAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        var token = timeout.Token;
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1000, Height = 660 };
        var target = new Uri("https://github.com/sharpninja/retro-ide");
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Uri? opened = null;
        window.ShowStartupSplash(uri =>
        {
            opened = uri;
            return Task.FromResult(true);
        }, startup.Task);
        window.Show();
        try
        {
            window.UpdateLayout();
            var link = window.GetVisualDescendants().OfType<HyperlinkButton>().Single(control =>
                string.Equals(AutomationProperties.GetAutomationId(control), "branding.splash.repository", StringComparison.Ordinal));
            if (window.Icon is null || window.FocusButton.IsEffectivelyEnabled || window.FocusButton.Bounds.Width <= 0 ||
                !string.Equals(link.Content as string, target.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("The splash did not show its link over the laid-out, disabled workbench.");
            window.ShowAttachmentPaths("Test MCP attachment");
            await CaptureAsync(window, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(window, "branding.splash.repository").ConfigureAwait(true);
            await ((IAsyncRelayCommand)link.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
            if (opened != target || !link.IsVisited)
                throw new InvalidOperationException("The splash hyperlink did not dispatch the expected GitHub URL.");
            if (!window.GetVisualDescendants().Contains(link))
                throw new InvalidOperationException("The splash closed before workspace readiness.");
            startup.SetResult();
            await WaitForWorkbenchAsync(window, token).ConfigureAwait(true);
            if (window.GetVisualDescendants().Contains(link) || !window.GetVisualDescendants().OfType<TextBox>()
                .Any(control => string.Equals(control.Text, "Test MCP attachment", StringComparison.Ordinal)))
                throw new InvalidOperationException("Readiness did not reveal the workbench and its MCP attachment status.");
            session.Layout.SelectDocument("start");
            while (!HasStartLink(window))
                await Task.Delay(20, token).ConfigureAwait(true);
        }
        finally { window.Close(); }

        await VerifyDefaultDismissalAsync(token).ConfigureAwait(true);
        Console.WriteLine("RemoteControl verified the native icon, splash GitHub link, readiness-based dismissal, and Start Page.");
    }

    private static bool HasStartLink(Window window) => window.GetVisualDescendants().OfType<HyperlinkButton>()
        .Any(control => control.IsEffectivelyVisible &&
            string.Equals(AutomationProperties.GetAutomationId(control), "branding.start.repository", StringComparison.Ordinal));

    private static async Task VerifyDefaultDismissalAsync(CancellationToken token)
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session);
        window.ShowStartupSplash();
        window.Show();
        try
        {
            await WaitForWorkbenchAsync(window, token).ConfigureAwait(true);
        }
        finally { window.Close(); }
    }

    private static async Task WaitForWorkbenchAsync(WorkbenchWindow window, CancellationToken token)
    {
        while (!window.FocusButton.IsEffectivelyEnabled)
            await Task.Delay(20, token).ConfigureAwait(true);
    }

    private static async Task CaptureAsync(Window window, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("RETROIDE_BRANDING_TEST_FRAME") is not { } path)
            return;
        var frames = new AvaloniaRenderTargetFrameProvider(
            Options.Create(new AvaloniaRemoteControlOptions { AllowRemoteFrames = true }), new AvaloniaUiThreadRemoteControlDispatcher());
        var frame = await frames.CaptureFrameAsync(window, 1, token).ConfigureAwait(true);
        if (frame is not { PixelWidth: > 0, PixelHeight: > 0, Png.Length: > 0 })
            throw new InvalidOperationException("The branded splash did not render to a native frame.");
        await File.WriteAllBytesAsync(path, frame.Png, token).ConfigureAwait(true);
    }
}
