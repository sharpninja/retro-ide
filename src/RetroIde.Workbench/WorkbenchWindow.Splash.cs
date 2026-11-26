using Avalonia.Controls;
using Avalonia.Threading;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchWindow
{
    private readonly Grid _surface;
    private readonly Control _desktopSurface;
    private BrandingPanel? _startupSplash;

    internal void ShowStartupSplash(Func<Uri, Task<bool>>? launch = null, TimeSpan? duration = null)
    {
        if (_startupSplash is not null)
            return;

        var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(3.2) };
        _startupSplash = new BrandingPanel(_session.Localization, _logger, Dismiss, launch);
        _desktopSurface.IsVisible = false;
        _surface.Children.Add(_startupSplash);
        Opened += Start;
        Closed += OnClosed;
        timer.Tick += OnTick;

        void Start(object? sender, EventArgs args)
        {
            _startupSplash?.ContinueButton?.Focus();
            timer.Start();
        }

        void OnClosed(object? sender, EventArgs args) => Stop();
        void OnTick(object? sender, EventArgs args) => Dismiss();

        void Stop()
        {
            timer.Stop();
            timer.Tick -= OnTick;
            Opened -= Start;
            Closed -= OnClosed;
        }

        void Dismiss()
        {
            if (_startupSplash is not { } splash)
                return;
            Stop();
            _startupSplash = null;
            _surface.Children.Remove(splash);
            _desktopSurface.IsVisible = true;
            _session.Layout.ShowFloatingLayouts();
        }
    }
}
