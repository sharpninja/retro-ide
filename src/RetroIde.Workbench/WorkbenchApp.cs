using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;

namespace RetroIde.Workbench;

/// <summary>Initializes Avalonia and the native workbench lifetime.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-contract-desktop-1</remarks>
public sealed class WorkbenchApp : Avalonia.Application
{
    private readonly ILoggerFactory _logs;
    private readonly ILogger _logger;
    private readonly bool _showStartupSplash;
    private Task<RunningMcp?>? _attachmentStartup;
    private Action? _stopDesktop;
    private Dispatcher? _dispatcher;
    internal static WorkbenchApp? Instance { get; private set; }

    /// <summary>Creates a workbench for Avalonia's application loader.</summary>
    public WorkbenchApp() : this(NullLoggerFactory.Instance, showStartupSplash: false) { }

    internal WorkbenchApp(ILoggerFactory logs, bool showStartupSplash = true)
    {
        _logs = logs;
        _logger = logs.CreateLogger<WorkbenchApp>();
        _showStartupSplash = showStartupSplash;
    }

    internal static string DefaultDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "RetroIde");

    /// <inheritdoc />
    public override void Initialize()
    {
        Instance = this;
        if (!ReferenceEquals(_logs, NullLoggerFactory.Instance))
        {
            _dispatcher = Dispatcher.UIThread;
            _dispatcher.UnhandledException += OnDispatcherException;
        }
        Name = BrandingPanel.ProductName;
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        // Keep the drag handle available after a tool becomes the only tab in a floating dock.
        Styles.Add(new Style(selector => selector.OfType<ToolTabStrip>().Class(":singleitem")
            .Template().OfType<ItemsPresenter>().Name("PART_ItemsPresenter"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, true) },
        });
        DataTemplates.Add(new FuncDataTemplate<ShellTool>((tool, _) =>
            tool?.Interaction.Wrap(tool.Id, CreateToolContent(tool))));
        DataTemplates.Add(new FuncDataTemplate<ShellDocument>((document, _) =>
            document?.Interaction.Wrap(document.Id, document switch
            {
                { Source: { } source } => DocumentSurface.Create(source),
                { GitDiff: { } diff } => GitDiffSurface.Create(diff),
                { GitHistory: { } history } => GitHistorySurface.Create(history),
                { GitBlame: { } blame } => GitBlameSurface.Create(blame),
                { Id: "start" } => new BrandingPanel(document.Localization, _logger),
                _ => PaneContent(document!.Localization, document.Id, document: true),
            })));
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var folder = Environment.GetEnvironmentVariable("RETROIDE_APP_DATA_DIR") ?? DefaultDataFolder;
            if (!Path.IsPathFullyQualified(folder))
                throw new InvalidOperationException("The Retro IDE application data directory must be an absolute path.");
            var layouts = new LayoutStore(Path.Combine(folder, "layout.json"), _logs.CreateLogger<LayoutStore>());
            var explorerStore = new WorkspaceExplorerStore(Path.Combine(folder, "workspace.json"), _logs.CreateLogger<WorkspaceExplorerStore>());
            var session = new WorkbenchSession(explorerStore, layouts.Load(), new WorkbenchLocalization(folder), _logs);
            var lifetimeCancellation = new CancellationTokenSource();
            _stopDesktop = () =>
            {
                Cleanup(lifetimeCancellation.Cancel, "desktop cancellation");
                Cleanup(session.Dispose, "workbench session shutdown");
                lifetimeCancellation.Dispose();
            };
            desktop.Exit += (_, _) => StopDesktop();
            var window = new WorkbenchWindow(session, layouts);
            if (_showStartupSplash)
                window.ShowStartupSplash();
            desktop.MainWindow = window;
            var monitor = desktop.Args?.Contains("--mcp", StringComparer.Ordinal) == true;
            var mutate = desktop.Args?.Contains("--mcp-mutate", StringComparer.Ordinal) == true;
            if (monitor || mutate)
                window.Opened += (_, _) => _attachmentStartup = StartMcpAsync(session, window, folder, mutate, lifetimeCancellation.Token);
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal void StopDesktop() => Interlocked.Exchange(ref _stopDesktop, null)?.Invoke();

    internal void DetachDispatcherExceptionHandler()
    {
        if (_dispatcher is { } dispatcher)
            dispatcher.UnhandledException -= OnDispatcherException;
        _dispatcher = null;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        var cancelled = args.Exception is OperationCanceledException { CancellationToken.IsCancellationRequested: true };
        RuntimeFailures.Report(_logger, args.Exception, "Avalonia dispatcher", cancelled ? LogLevel.Warning : LogLevel.Critical);
        if (cancelled)
            args.Handled = true;
    }

    private void Cleanup(Action action, string boundary)
    {
        try { action(); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, boundary);
        }
    }

    internal async Task StopMcpAsync()
    {
        if (_attachmentStartup is not null && await _attachmentStartup.ConfigureAwait(false) is { } running)
        {
            try { await running.Host.DisposeAsync().ConfigureAwait(false); }
            finally { DeleteAttachment(running.Path); }
        }
    }

    private async Task<RunningMcp?> StartMcpAsync(WorkbenchSession session, WorkbenchWindow window, string folder, bool mutate, CancellationToken token)
    {
        LoopbackMcpHost? host = null;
        string? writtenPath = null;
        try
        {
            await session.GitRestoration.WaitAsync(token).ConfigureAwait(false);
            host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var directory = Directory.CreateDirectory(Path.Combine(folder, "attachments")).FullName;
            var path = Path.Combine(directory, $"{session.View.ViewKey:N}.json");
            var descriptor = host.CreateAttachment(mutate ? "local-editor" : "local-observer", canMutate: mutate);
            descriptor.Write(path);
            writtenPath = path;
            token.ThrowIfCancellationRequested();
            var running = new RunningMcp(host, path);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => window.ShowAttachmentPaths($"MCP attachment file  {path}"));
            host = null;
            return running;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "optional MCP attachment startup");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => window.ShowAttachmentPaths("Unable to start the local MCP attachment."));
        }
        finally
        {
            if (host is not null)
            {
                try { await host.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) when (RuntimeFailures.CanRecover(error))
                {
                    RuntimeFailures.Report(_logger, error, "incomplete MCP attachment cleanup");
                }
                finally
                {
                    if (writtenPath is not null)
                        DeleteAttachment(writtenPath);
                }
            }
        }
        return null;
    }

    private void DeleteAttachment(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            RuntimeFailures.Report(_logger, error, "MCP attachment file cleanup", LogLevel.Warning);
        }
    }

    private sealed record RunningMcp(LoopbackMcpHost Host, string Path);

    private static Control CreateToolContent(ShellTool tool) => tool switch
    {
        { Explorer: { } explorer } => ExplorerPane.Create(explorer),
        { SourceControl: { } sourceControl } => SourceControlPane.Create(sourceControl),
        _ => PaneContent(tool.Localization, tool.Id, document: false),
    };

    private static Control PaneContent(WorkbenchLocalization localization, string id, bool document)
    {
        var title = new TextBlock { FontSize = document ? 24 : 14, FontWeight = FontWeight.SemiBold };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        localization.Observe(title, () => title.Text = localization.Text(WorkbenchText.PaneTitle(id)));
        localization.Observe(message, () => message.Text = localization.Text(WorkbenchText.PaneDescription(id)));
        var panel = new StackPanel
        {
            Margin = new Thickness(document ? 28 : 12),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                title,
                message,
            },
        };
        return new Border { Child = panel };
    }
}
