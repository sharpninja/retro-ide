using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using RetroIde.Application;

namespace RetroIde.Workbench;

internal static class TerminalSurface
{
    internal const string AutomationId = "git.terminal.surface";

    internal static Control Create(TerminalViewModel view)
    {
        var surface = new Border();
        TerminalControl? control = null;
        TerminalAttachment? connection = null;
        surface.Loaded += (_, _) =>
        {
            if (control is not null)
                return;
            control = new TerminalControl { AllowWindowOps = false, FontSize = 13, DetectUrls = false };
            AutomationProperties.SetAutomationId(control, AutomationId);
            control.Loaded += Attach;
            surface.Child = control;
        };
        surface.Unloaded += (_, _) =>
        {
            if (control is null)
                return;
            control.Loaded -= Attach;
            control.DetachConnection();
            connection?.Dispose();
            connection = null;
            control.Dispose();
            control = null;
            surface.Child = null;
        };
        return surface;

        void Attach(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            if (!ReferenceEquals(sender, control) || connection is not null)
                return;
            var attached = new TerminalAttachment(view.Terminal);
            try
            {
                control!.AttachConnection(attached);
                connection = attached;
                control.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault()?.Focus();
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                attached.Dispose();
                view.Terminal.ReportFailure(error, "Open repository terminal presentation");
            }
        }
    }
}
