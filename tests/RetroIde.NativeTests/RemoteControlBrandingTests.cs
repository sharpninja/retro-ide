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
        Uri? opened = null;
        window.ShowStartupSplash(uri =>
        {
            opened = uri;
            return Task.FromResult(true);
        }, TimeSpan.FromMinutes(1));
        window.Show();
        try
        {
            window.UpdateLayout();
            var link = window.GetVisualDescendants().OfType<HyperlinkButton>().Single(control =>
                string.Equals(AutomationProperties.GetAutomationId(control), "branding.splash.repository", StringComparison.Ordinal));
            if (window.Icon is null || window.FocusButton.IsEffectivelyVisible ||
                !string.Equals(link.Content as string, target.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("The startup splash did not show a branded window and its repository link.");
            await CaptureAsync(window, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(window, "branding.splash.repository").ConfigureAwait(true);
            await ((IAsyncRelayCommand)link.Command!).ExecutionTask!.WaitAsync(token).ConfigureAwait(true);
            if (opened != target || !link.IsVisited)
                throw new InvalidOperationException("The splash hyperlink did not dispatch the expected GitHub URL.");
            await WorkbenchRemoteControl.ClickAsync(window, "branding.splash.continue").ConfigureAwait(true);
            window.UpdateLayout();
            if (!window.FocusButton.IsEffectivelyVisible || window.GetVisualDescendants().Contains(link))
                throw new InvalidOperationException("Continue did not remove the splash and reveal the workbench.");
            session.Layout.SelectDocument("start");
            while (!HasStartLink(window))
                await Task.Delay(20, token).ConfigureAwait(true);
        }
        finally { window.Close(); }

        await VerifyTimedDismissalAsync(token).ConfigureAwait(true);
        Console.WriteLine("RemoteControl verified the native window icon, splash GitHub link, Continue, Start Page, and timed dismissal.");
    }

    private static bool HasStartLink(Window window) => window.GetVisualDescendants().OfType<HyperlinkButton>()
        .Any(control => control.IsEffectivelyVisible &&
            string.Equals(AutomationProperties.GetAutomationId(control), "branding.start.repository", StringComparison.Ordinal));

    private static async Task VerifyTimedDismissalAsync(CancellationToken token)
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session);
        window.ShowStartupSplash(duration: TimeSpan.FromMilliseconds(100));
        window.Show();
        try
        {
            while (!window.FocusButton.IsEffectivelyVisible)
                await Task.Delay(20, token).ConfigureAwait(true);
        }
        finally { window.Close(); }
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
