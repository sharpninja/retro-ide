using System.Globalization;
using Microsoft.Extensions.Logging;

namespace RetroIde.Workbench;

internal sealed class DesktopErrorLog : ILoggerProvider
{
    private readonly Lock _write = new();
    private readonly string _directory;
    private readonly string _fileName = $"desktop-{Environment.ProcessId}-{Guid.NewGuid():N}.log";

    internal DesktopErrorLog(string appData) => _directory = Path.Combine(appData, "logs");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private void Append(string message)
    {
        lock (_write)
        {
            Directory.CreateDirectory(_directory);
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(Path.Combine(_directory, _fileName), options);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(message);
        }
    }

    private sealed class FileLogger(DesktopErrorLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level is >= LogLevel.Warning and < LogLevel.None;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> format)
        {
            if (IsEnabled(level))
                owner.Append($"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level}] {category}[{id.Id}] {format(state, exception)}{Environment.NewLine}{exception}");
        }
    }
}
