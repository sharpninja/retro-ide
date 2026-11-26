using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace RetroIde.Application;

internal static partial class RuntimeFailures
{
    internal static bool CanRecover(Exception error) =>
        error is not (OutOfMemoryException or AccessViolationException or StackOverflowException or SEHException or InvalidProgramException) &&
        (error is not AggregateException aggregate
            ? error.InnerException is null || CanRecover(error.InnerException)
            : aggregate.InnerExceptions.All(CanRecover));

    internal static void Report(ILogger logger, Exception error, string boundary, LogLevel level = LogLevel.Error)
    {
        try { Write(logger, level, boundary, error); }
        catch (Exception loggingError) when (CanRecover(loggingError))
        {
            try { Console.Error.WriteLine($"Retro IDE logging failed for {boundary}.{Environment.NewLine}{error}{Environment.NewLine}{loggingError}"); }
            catch (Exception outputError) when (CanRecover(outputError)) { }
        }
    }

    [LoggerMessage(EventId = 2100, Message = "Retro IDE failure in {Boundary}.")]
    private static partial void Write(ILogger logger, LogLevel level, string boundary, Exception error);
}

/// <summary>Records process-level fallbacks for failures outside an owned operation.</summary>
internal sealed class ProcessExceptionHandlers : IDisposable
{
    private readonly ILogger _logger;

    internal ProcessExceptionHandlers(ILogger logger)
    {
        _logger = logger;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var error = args.ExceptionObject as Exception ?? new InvalidOperationException("The runtime raised a non-exception object.");
        RuntimeFailures.Report(_logger, error, "unhandled process exception", LogLevel.Critical);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        RuntimeFailures.Report(_logger, args.Exception, "unobserved background task", LogLevel.Critical);
        if (RuntimeFailures.CanRecover(args.Exception))
            args.SetObserved();
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}
