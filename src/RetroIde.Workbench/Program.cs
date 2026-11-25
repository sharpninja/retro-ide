using Avalonia;

namespace RetroIde.Workbench;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return AppBuilder.Configure<WorkbenchApp>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // The desktop dispatcher has stopped. Run the async host shutdown without its synchronization context.
            if (WorkbenchApp.Instance is { } app)
                Task.Run(app.StopMcpAsync).GetAwaiter().GetResult();
        }
    }
}
