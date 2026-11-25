using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlDesktop
{
    internal static AppBuilder BuildApp()
    {
        var app = AppBuilder.Configure<WorkbenchApp>().UsePlatformDetect();
        if (OperatingSystem.IsWindows())
        {
            // Session 0 has native windows but no hardware composition desktop.
            app.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface],
            });
        }
        return app;
    }

    internal static int Run()
    {
        try
        {
            return BuildApp().StartWithClassicDesktopLifetime(["--mcp-mutate"], lifetime =>
                lifetime.Startup += (_, _) => _ = Task.Run(() => RunCommandsAsync(lifetime)));
        }
        finally
        {
            if (WorkbenchApp.Instance is { } app)
                Task.Run(app.StopMcpAsync).GetAwaiter().GetResult();
        }
    }

    private static async Task RunCommandsAsync(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            while (string.Equals(await Console.In.ReadLineAsync().ConfigureAwait(false), "focus", StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                    WorkbenchRemoteControl.ClickAsync(lifetime.MainWindow!, "workbench.view.focus.toolbar"))
                    .GetTask().Unwrap().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => lifetime.MainWindow?.Close()).GetTask().ConfigureAwait(false);
        }
    }
}
