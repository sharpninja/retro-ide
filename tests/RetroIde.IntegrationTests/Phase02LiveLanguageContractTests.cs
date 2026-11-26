using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Checks live localized controls before and after opening a new workbench.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-008#ac-test-live-language. Share the string, image, accessibility, direction and preference assertions.</remarks>
public sealed partial class Phase02LiveLanguageContractTests
{
    private const string Focus = "Focus tool pane";
    private const string Folder = "Open Folder…";
    private const string Explorer = "Project Explorer";
    private const string ArabicFocus = "تركيز النافذة";
    private const string ArabicFolder = "افتح مجلدًا";
    private const string ArabicExplorer = "المستكشف";
    private const string PseudoFocus = "[!! Fóócús tóól pááné fór lóngér tééstíng !!]";

    /// <summary>Proves the reference workflow detects stale presentation, bindings, and persisted language.</summary>
    [Theory]
    [InlineData("stale-label")]
    [InlineData("stale-accessibility")]
    [InlineData("stale-icon")]
    [InlineData("main-ltr")]
    [InlineData("floating-ltr")]
    [InlineData("clipped-pseudo-label")]
    [InlineData("changed-command")]
    [InlineData("changed-binding")]
    [InlineData("shortcut-stops-dispatching")]
    [InlineData("lost-preference")]
    public async Task Reference_detects_live_language_and_persistence_faults(string fault)
    {
        using var clean = new LanguageFiles();
        await VerifyLanguageAsync(new ReferenceLanguage(clean));
        using var broken = new LanguageFiles();
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyLanguageAsync(new ReferenceLanguage(broken, fault)));
    }

    private static async Task VerifyLanguageAsync(ILanguage workbench)
    {
        var original = await workbench.OpenAsync();
        Assert.Equal("en", original.Locale);
        AssertText(original, Focus, Folder, Explorer);
        Assert.False(original.MainRtl);
        Assert.False(original.FloatingRtl);
        Assert.False(original.CustomIcon);
        Assert.True(original.LabelFits);
        Assert.NotEmpty(original.Binding);

        var arabic = await workbench.ChangeAsync("ar");
        Assert.Equal("ar", arabic.Locale);
        AssertText(arabic, ArabicFocus, ArabicFolder, ArabicExplorer);
        Assert.True(arabic.MainRtl);
        Assert.True(arabic.FloatingRtl);
        Assert.True(arabic.CustomIcon);
        Assert.True(arabic.LabelFits);
        Assert.Same(original.Command, arabic.Command);
        Assert.Equal(original.Binding, arabic.Binding);
        Assert.Equal(1, await workbench.InvokeShortcutAsync());

        var pseudo = await workbench.ChangeAsync("qps-ploc");
        Assert.Equal("qps-ploc", pseudo.Locale, ignoreCase: true);
        AssertText(pseudo, PseudoFocus, Folder, Explorer);
        Assert.False(pseudo.MainRtl);
        Assert.False(pseudo.FloatingRtl);
        Assert.False(pseudo.CustomIcon);
        Assert.True(pseudo.LabelFits);
        Assert.True(pseudo.LabelWidth > original.LabelWidth);
        Assert.Same(original.Command, pseudo.Command);
        Assert.Equal(original.Binding, pseudo.Binding);
        Assert.Equal(1, await workbench.InvokeShortcutAsync());

        var reopened = await workbench.ReopenAsync();
        Assert.Equal("qps-ploc", reopened.Locale, ignoreCase: true);
        AssertText(reopened, PseudoFocus, Folder, Explorer);
        Assert.Equal(original.Binding, reopened.Binding);
        Assert.True(reopened.LabelFits);
    }

    private static void AssertText(LanguageState snapshot, string focus, string folder, string explorer)
    {
        Assert.Equal(focus, snapshot.FocusLabel);
        Assert.Equal(focus, snapshot.ToolbarName);
        Assert.Equal(focus, snapshot.MenuName);
        Assert.Equal(folder, snapshot.FolderName);
        Assert.Equal(explorer, snapshot.ExplorerTitle);
    }

    private interface ILanguage
    {
        Task<LanguageState> OpenAsync();
        Task<LanguageState> ChangeAsync(string locale);
        Task<int> InvokeShortcutAsync();
        Task<LanguageState> ReopenAsync();
    }

    private sealed record LanguageState(string Locale, string FocusLabel, string ToolbarName, string MenuName, string FolderName,
        string ExplorerTitle, bool MainRtl, bool FloatingRtl, bool CustomIcon, double LabelWidth, bool LabelFits, object Command, string Binding);

    private sealed class LanguageFiles : IDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("retro-ide-live-language-");

        public LanguageFiles()
        {
            var rtl = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["command.workbench.view.focus.label"] = ArabicFocus,
                ["command.workspace.open-folder.label"] = ArabicFolder,
                ["pane.explorer.title"] = ArabicExplorer,
            };
            var pictures = new Dictionary<string, string>(StringComparer.Ordinal) { ["command.workbench.view.focus.icon"] = "icons/focus.svg" };
            WritePack("ar", rtl, pictures);
            var assets = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Manifest("ar"))!, "icons"));
            File.WriteAllText(Path.Combine(assets.FullName, "focus.svg"), """
                <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18"><rect width="18" height="18" fill="#835fee"/></svg>
                """);
            WritePack("qps-ploc", new(StringComparer.Ordinal) { ["command.workbench.view.focus.label"] = PseudoFocus }, new(StringComparer.Ordinal));
        }

        public string Home => _folder.FullName;
        public string Preference => Path.Combine(Home, "language.txt");
        public string Manifest(string locale) => Path.Combine(Home, "locales", locale, "manifest.json");

        private void WritePack(string locale, Dictionary<string, string> entries, Dictionary<string, string> images)
        {
            var manifest = Manifest(locale);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { locale, version = 1, text = entries, images }));
        }

        public void Dispose() => _folder.Delete(recursive: true);
    }

    private sealed class ReferenceLanguage(LanguageFiles files, string? fault = null) : ILanguage
    {
        private readonly object _command = new();

        public Task<LanguageState> OpenAsync() => Task.FromResult(Observe("en"));

        public async Task<LanguageState> ChangeAsync(string locale)
        {
            if (!Fault("lost-preference"))
                await File.WriteAllTextAsync(files.Preference, locale, TestContext.Current.CancellationToken);
            return Observe(locale);
        }

        public Task<int> InvokeShortcutAsync() => Task.FromResult(Fault("shortcut-stops-dispatching") ? 0 : 1);

        public async Task<LanguageState> ReopenAsync() => Observe(File.Exists(files.Preference)
            ? await File.ReadAllTextAsync(files.Preference, TestContext.Current.CancellationToken) : "en");

        private LanguageState Observe(string locale)
        {
            var english = string.Equals(locale, "en", StringComparison.Ordinal);
            using var catalog = english ? null : JsonDocument.Parse(File.ReadAllText(files.Manifest(locale)));
            var text = catalog?.RootElement.GetProperty("text");
            string Label(string slug, string fallback) => text is { } entries && entries.TryGetProperty(slug, out var value)
                ? value.GetString()! : fallback;
            var focus = Label("command.workbench.view.focus.label", Focus);
            var folder = Label("command.workspace.open-folder.label", Folder);
            var explorer = Label("pane.explorer.title", Explorer);
            var rtl = string.Equals(locale, "ar", StringComparison.Ordinal);
            var custom = catalog?.RootElement.GetProperty("images").TryGetProperty("command.workbench.view.focus.icon", out _) == true;
            return new LanguageState(locale, rtl && Fault("stale-label") ? Focus : focus, focus,
                rtl && Fault("stale-accessibility") ? Focus : focus, folder, explorer,
                rtl && !Fault("main-ltr"), rtl && !Fault("floating-ltr"), custom && !Fault("stale-icon"), focus.Length * 8,
                rtl || english || !Fault("clipped-pseudo-label"), rtl && Fault("changed-command") ? new object() : _command,
                rtl && Fault("changed-binding") ? "Ctrl+F" : "Ctrl+Shift+F");
        }

        private bool Fault(string value) => string.Equals(value, fault, StringComparison.Ordinal);
    }
}
