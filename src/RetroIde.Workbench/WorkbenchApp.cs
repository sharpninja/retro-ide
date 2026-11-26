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
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;

namespace RetroIde.Workbench;

/// <summary>Initializes Avalonia and the native workbench lifetime.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-contract-desktop-1</remarks>
public sealed class WorkbenchApp : Avalonia.Application
{
    private Task<RunningMcp?>? _attachmentStartup;

    internal static WorkbenchApp? Instance { get; private set; }

    /// <inheritdoc />
    public override void Initialize()
    {
        Instance = this;
        Name = "Retro IDE";
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
            tool?.Interaction.Wrap(tool.Id, tool.Explorer is { } explorer
                ? ExplorerPane.Create(explorer)
                : PaneContent(tool.Localization, tool.Id, document: false))));
        DataTemplates.Add(new FuncDataTemplate<ShellDocument>((document, _) =>
            document?.Interaction.Wrap(document.Id, document.Source is { } source
                ? DocumentSurface.Create(source)
                : PaneContent(document.Localization, document.Id, document: true))));
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var folder = Environment.GetEnvironmentVariable("RETROIDE_APP_DATA_DIR") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "RetroIde");
            if (!Path.IsPathFullyQualified(folder))
                throw new InvalidOperationException("The Retro IDE application data directory must be an absolute path.");
            var layouts = new LayoutStore(Path.Combine(folder, "layout.json"));
            var explorerStore = new WorkspaceExplorerStore(Path.Combine(folder, "workspace.json"));
            var session = new WorkbenchSession(explorerStore, layouts.Load(), new WorkbenchLocalization(folder));
            var window = new WorkbenchWindow(session, layouts);
            desktop.MainWindow = window;
            var monitor = desktop.Args?.Contains("--mcp", StringComparer.Ordinal) == true;
            var mutate = desktop.Args?.Contains("--mcp-mutate", StringComparer.Ordinal) == true;
            var lifetimeCancellation = new CancellationTokenSource();
            if (monitor || mutate)
                window.Opened += (_, _) => _attachmentStartup = StartMcpAsync(session, window, folder, mutate, lifetimeCancellation.Token);
            desktop.Exit += (_, _) =>
            {
                lifetimeCancellation.Cancel();
                session.Dispose();
                lifetimeCancellation.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal async Task StopMcpAsync()
    {
        if (_attachmentStartup is not null && await _attachmentStartup.ConfigureAwait(false) is { } running)
        {
            try { await running.Host.DisposeAsync().ConfigureAwait(false); }
            finally { DeleteAttachment(running.Path); }
        }
    }

    private static async Task<RunningMcp?> StartMcpAsync(WorkbenchSession session, WorkbenchWindow window, string folder, bool mutate, CancellationToken token)
    {
        LoopbackMcpHost? host = null;
        string? writtenPath = null;
        try
        {
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => window.ShowAttachmentPaths("Unable to start the local MCP attachment."));
        }
        finally
        {
            if (host is not null)
            {
                try { await host.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    if (writtenPath is not null)
                        DeleteAttachment(writtenPath);
                }
            }
        }
        return null;
    }

    private static void DeleteAttachment(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record RunningMcp(LoopbackMcpHost Host, string Path);

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
