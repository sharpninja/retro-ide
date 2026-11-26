using System.Globalization;
using Avalonia.Input;
using RetroIde.Application;
using RetroIde.Application.Localization;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal static class WorkbenchCommands
{
    public static readonly CommandDescriptor FocusTool = Create(FeatureRegistry.FocusToolId, "Ctrl+Shift+F");
    public static readonly CommandDescriptor OpenFolder = Create("workspace.open-folder", "Ctrl+Shift+O");
    public static readonly CommandDescriptor OpenProject = Create("workspace.open-project", "Ctrl+Alt+O");
    public static readonly CommandDescriptor Save = Create("document.save", "Ctrl+S");
    public static readonly CommandDescriptor Reveal = Create("document.reveal", "Ctrl+Alt+R");
    public static readonly CommandDescriptor Search = Create("document.search", "Ctrl+F");
    public static readonly CommandDescriptor Next = Create("document.next", "F3");
    public static readonly CommandDescriptor Previous = Create("document.previous", "Shift+F3");
    public static readonly CommandDescriptor Replace = Create("document.replace", "Ctrl+Alt+H");
    public static readonly CommandDescriptor ReplaceAll = Create("document.replace-all", "Ctrl+Alt+Shift+H");
    public static readonly CommandDescriptor Undo = Create("document.undo", "Ctrl+Z");
    public static readonly CommandDescriptor Redo = Create("document.redo", "Ctrl+Y");
    public static readonly CommandDescriptor Close = Create("document.close", "Ctrl+W");
    public static readonly CommandDescriptor Discard = Create("document.discard", "Ctrl+Alt+Shift+W");
    public static readonly CommandDescriptor OpenSourceControl = Create("git.source-control.focus", "Ctrl+Alt+G");
    public static readonly CommandDescriptor SelectRepository = Create("git.repository.select", "Ctrl+Alt+Shift+G");
    public static readonly CommandDescriptor RefreshGitStatus = Create("git.status.read", "Ctrl+Alt+Shift+R");
    public static readonly CommandDescriptor InitializeRepository = Create("git.repository.initialize", "Ctrl+Alt+I");
    public static readonly CommandDescriptor CloneRepository = Create("git.repository.clone", "Ctrl+Alt+Shift+C");

    // A document key binding can be active inside a main or floating workbench window.
    // All commands in this catalog can share that input route, so check them together.
    private static readonly CommandDescriptor[] Commands =
        [FocusTool, OpenFolder, OpenProject, Save, Reveal, Search, Next, Previous, Replace, ReplaceAll, Undo, Redo, Close, Discard,
            OpenSourceControl, SelectRepository, RefreshGitStatus, InitializeRepository, CloneRepository];

    internal static void ValidateBundledResources()
    {
        using var english = new WorkbenchLocalization(initial: CultureInfo.GetCultureInfo("en"));
        foreach (var slug in WorkbenchText.Required.Concat(DockLayout.RequiredPaneText()))
            if (string.IsNullOrWhiteSpace(english.Text(slug)))
                throw new InvalidDataException($"The required English resource '{slug}' is empty.");
        _ = WorkbenchText.ExplorerRoot(english, WorkspaceExplorer.RootKind.Folder, "example", unavailable: false);
        _ = WorkbenchText.ExplorerRoot(english, WorkspaceExplorer.RootKind.Project, "example", unavailable: true);
        Validate(Commands, english);
    }

    internal static void Validate(IReadOnlyList<CommandDescriptor> descriptors, WorkbenchLocalization english)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var defaults = new Dictionary<KeyGesture, string>();
        var windows = new Dictionary<KeyGesture, string>();
        var linux = new Dictionary<KeyGesture, string>();
        foreach (var descriptor in descriptors)
        {
            if (!LanguagePack.IsResourceSlug(descriptor.Id) || !ids.Add(descriptor.Id))
                throw new InvalidDataException($"Invalid or duplicate command ID '{descriptor.Id}'.");
            if (!LanguagePack.IsResourceSlug(descriptor.LabelSlug) || !LanguagePack.IsResourceSlug(descriptor.IconSlug))
                throw new InvalidDataException($"Command '{descriptor.Id}' requires valid text and image slugs.");
            if (string.IsNullOrWhiteSpace(english.Text(descriptor.LabelSlug)))
                throw new InvalidDataException($"Command '{descriptor.Id}' requires a nonempty English label.");
            english.Image(descriptor.IconSlug);

            AddGesture(descriptor, descriptor.DefaultGesture, "default", defaults);
            AddGesture(descriptor, descriptor.GestureFor(windows: true), "Windows", windows);
            AddGesture(descriptor, descriptor.GestureFor(windows: false), "Linux", linux);
        }
        if (english.Diagnostics is { Count: > 0 } diagnostics)
            throw new InvalidDataException("Bundled localization resources failed validation: " +
                string.Join(", ", diagnostics.Select(issue => $"{issue.Kind} '{issue.Slug}' ({issue.Code})").Order(StringComparer.Ordinal)));
    }

    private static void AddGesture(CommandDescriptor descriptor, string gesture, string platform, Dictionary<KeyGesture, string> assigned)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            throw new InvalidDataException($"Command '{descriptor.Id}' requires a {platform} gesture.");
        KeyGesture parsed;
        try { parsed = KeyGesture.Parse(gesture); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            throw new InvalidDataException($"Command '{descriptor.Id}' has an invalid {platform} gesture '{gesture}'.", error);
        }
        if (parsed.Key == Key.None || !Enum.IsDefined(parsed.Key))
            throw new InvalidDataException($"Command '{descriptor.Id}' has an invalid {platform} gesture '{gesture}'.");
        if (!assigned.TryAdd(parsed, descriptor.Id))
            throw new InvalidDataException($"Commands '{assigned[parsed]}' and '{descriptor.Id}' share {platform} gesture '{parsed}'.");
    }

    private static CommandDescriptor Create(string id, string gesture) => new(id, $"command.{id}.label", $"command.{id}.icon", gesture);
}
