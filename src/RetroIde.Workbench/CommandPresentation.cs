using System.Globalization;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Media;
using RetroIde.Application;

namespace RetroIde.Workbench;

internal sealed record CommandDescriptor(string Id, string LabelSlug, string IconSlug, string DefaultGesture,
    string? WindowsGesture = null, string? LinuxGesture = null)
{
    public string EffectiveGesture => (OperatingSystem.IsWindows() ? WindowsGesture : LinuxGesture) ?? DefaultGesture;
}

internal sealed record CommandPresentation(string Label, Geometry IconGeometry, KeyGesture Gesture, string Shortcut)
{
    // TEST-RETRO-008#ac-test-gesture-parity
    public static CommandPresentation Create(CommandDescriptor descriptor)
    {
        using var resource = typeof(CommandPresentation).Assembly.GetManifestResourceStream("RetroIde.Workbench.Resources.en.json")
            ?? throw new InvalidOperationException("The bundled English resource catalog is missing.");
        using var catalog = JsonDocument.Parse(resource);
        var root = catalog.RootElement;
        var label = root.GetProperty("text").GetProperty(descriptor.LabelSlug).GetString()!;
        var icon = Geometry.Parse(root.GetProperty("icons").GetProperty(descriptor.IconSlug).GetString()!);
        var gesture = KeyGesture.Parse(descriptor.EffectiveGesture);
        return new CommandPresentation(label, icon, gesture, gesture.ToString(null, CultureInfo.CurrentCulture));
    }

    public static CommandDescriptor Focus(FeatureRegistry registry) =>
        new(registry.FocusTool.Id, "command.workbench.view.focus.label", "command.workbench.view.focus.icon", "Ctrl+Shift+F");
}
