using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Localization;

namespace RetroIde.Workbench;

internal sealed record CommandDescriptor(string Id, string LabelSlug, string IconSlug, string DefaultGesture,
    string? WindowsGesture = null, string? LinuxGesture = null)
{
    public string EffectiveGesture => GestureFor(OperatingSystem.IsWindows());

    public string GestureFor(bool windows) => (windows ? WindowsGesture : LinuxGesture) ?? DefaultGesture;
}

internal sealed record CommandPresentation(string Label, Geometry IconGeometry, KeyGesture Gesture, string Shortcut, IImage? Image = null)
{
    // TEST-RETRO-008#ac-test-gesture-parity
    public static CommandPresentation Create(CommandDescriptor descriptor, WorkbenchLocalization? localization = null)
    {
        var catalog = localization ?? WorkbenchLocalization.English;
        var label = catalog.Text(descriptor.LabelSlug);
        IImage? image = null;
        Geometry? geometry = null;
        var resource = catalog.Image(descriptor.IconSlug, candidate =>
        {
            if (candidate.Kind != LocaleCatalog.ImageKind.Geometry)
                return (image = catalog.Decode(candidate)) is not null;

            try { geometry = Geometry.Parse(candidate.Geometry!); return true; }
            catch (FormatException) { return false; }
        });
        geometry ??= Geometry.Parse(resource.Geometry ?? "M2,2 L18,2 L18,18 L2,18 Z");
        var gesture = KeyGesture.Parse(descriptor.EffectiveGesture);
        return new CommandPresentation(label, geometry, gesture, gesture.ToString(null, catalog.FormattingCulture), image);
    }

    public Control CreateIcon(double size = 14) => Image is { } source
        ? new Image { Width = size, Height = size, Source = source }
        : new PathIcon { Width = size, Height = size, Data = IconGeometry };

    public static MenuItem CreateMenuItem(CommandDescriptor descriptor, ICommand command, WorkbenchLocalization localization)
    {
        var item = new MenuItem { Command = command };
        AutomationProperties.SetAutomationId(item, descriptor.Id + ".menu");
        localization.Observe(item, () =>
        {
            var presentation = Create(descriptor, localization);
            item.Header = presentation.Label;
            item.Icon = presentation.CreateIcon();
            item.InputGesture = presentation.Gesture;
            AutomationProperties.SetName(item, presentation.Label);
        });
        return item;
    }

    public static Button CreateButton(CommandDescriptor descriptor, ICommand command, WorkbenchLocalization localization)
    {
        var button = new Button { Command = command };
        AutomationProperties.SetAutomationId(button, descriptor.Id + ".toolbar");
        localization.Observe(button, () =>
        {
            var presentation = Create(descriptor, localization);
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { presentation.CreateIcon(), new TextBlock { Text = presentation.Label } },
            };
            ToolTip.SetTip(button, $"{presentation.Label} ({presentation.Shortcut})");
            AutomationProperties.SetName(button, presentation.Label);
        });
        return button;
    }
}
