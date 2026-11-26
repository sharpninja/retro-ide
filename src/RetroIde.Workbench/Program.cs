using Avalonia;

namespace RetroIde.Workbench;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--validate-bundled-resources"])
        {
            try
            {
                WorkbenchCommands.ValidateBundledResources();
                return 0;
            }
            catch (InvalidDataException error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

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
