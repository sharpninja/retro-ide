using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using RetroIde.Application;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchWindow
{
    private readonly Grid _surface;
    private readonly DockPanel _desktopSurface;
    private BrandingPanel? _startupSplash;

    internal void ShowStartupSplash(Func<Uri, Task<bool>>? launch = null, Task? startupReady = null)
    {
        if (_startupSplash is not null)
            return;

        _startupSplash = new BrandingPanel(_session.Localization, _logger, startup: true, launch);
        _desktopSurface.IsEnabled = false;
        _surface.Children.Add(_startupSplash);
        var cancellation = new CancellationTokenSource();
        Opened += Start;
        Closed += OnClosed;

        async void Start(object? sender, EventArgs args)
        {
            Opened -= Start;
            await DismissWhenReadyAsync(startupReady ?? _session.GitRestoration, cancellation.Token).ConfigureAwait(true);
        }

        void OnClosed(object? sender, EventArgs args)
        {
            Opened -= Start;
            Closed -= OnClosed;
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private async Task DismissWhenReadyAsync(Task startupReady, CancellationToken token)
    {
        try
        {
            await startupReady.WaitAsync(token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _session.Layout.ShowFloatingLayouts();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            token.ThrowIfCancellationRequested();
            UpdateLayout();
            if (ElementComposition.GetElementVisual(_desktopSurface) is { } visual)
                await visual.Compositor.RequestCommitAsync().WaitAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "complete the startup splash");
        }
        if (token.IsCancellationRequested || _closed || _startupSplash is not { } splash)
            return;

        _startupSplash = null;
        _surface.Children.Remove(splash);
        _desktopSurface.IsEnabled = true;
    }
}
