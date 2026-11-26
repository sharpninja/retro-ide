using Avalonia;
using Microsoft.Extensions.Logging;
using RetroIde.Application;

namespace RetroIde.Workbench;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var data = Environment.GetEnvironmentVariable("RETROIDE_APP_DATA_DIR");
        var logData = data is not null && Path.IsPathFullyQualified(data) ? data : WorkbenchApp.DefaultDataFolder;
        using var logs = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
            .AddProvider(new DesktopErrorLog(logData)));
        var logger = logs.CreateLogger("RetroIde.Workbench");
        using var exceptions = new ProcessExceptionHandlers(logger);
        WorkbenchApp? app = null;
        var exitCode = 1;
        try
        {
            if (args is ["--validate-bundled-resources"])
            {
                WorkbenchCommands.ValidateBundledResources();
                exitCode = 0;
            }
            else
                exitCode = AppBuilder.Configure(() => app = new WorkbenchApp(logs))
                    .UsePlatformDetect()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            RuntimeFailures.Report(logger, error, "desktop entry point", LogLevel.Critical);
        }
        finally
        {
            if (app is not null)
            {
                try { app.StopDesktop(); }
                catch (Exception error)
                {
                    RuntimeFailures.Report(logger, error, "desktop shutdown", LogLevel.Critical);
                    exitCode = 1;
                }
                try { Task.Run(app.StopMcpAsync).GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    RuntimeFailures.Report(logger, error, "desktop MCP shutdown", LogLevel.Critical);
                    exitCode = 1;
                }
                app.DetachDispatcherExceptionHandler();
            }
        }
        return exitCode;
    }
}
