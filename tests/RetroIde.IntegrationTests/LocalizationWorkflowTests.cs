using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using RetroIde.Application.Localization;
using RetroIde.Mcp;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies versioned language resources and live workbench language changes.</summary>
public sealed class LocalizationWorkflowTests
{
    /// <summary>Each resource resolves independently across exact, parent, and English catalogs.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-resource-fallback; TEST-RETRO-008#ac-test-pack-validation</remarks>
    [Fact]
    public void Partial_packs_resolve_text_and_images_with_diagnostics_for_invalid_entries()
    {
        using var fixture = new LocaleFixture();
        using var english = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"locale":"en","version":1,"text":{"label.base":"Default","label.parent":"Parent default"},
              "icons":{"icon.open":"M0,0 L8,0 L0,8 Z","icon.fallback":"M0,0 L4,4 L0,4 Z"}}
            """));
        var catalog = new LocaleCatalog(english);
        var fr = fixture.Pack("fr", """
            {"locale":"fr","version":1,"text":{"label.parent":"Parent français"},
               "images":{"icon.open":"icons/open.svg","icon.fallback":"../outside.svg"}}
            """);
        WriteIcon(fr);
        var exact = fixture.Pack("fr-CA", """
            {"locale":"fr-CA","version":1,"text":{"label.exact":"Québec","label.parent":4},
              "images":{"icon.open":"invalid.svg","icon.exact":"exact.png"}}
            """);
        File.WriteAllText(Path.Combine(exact, "invalid.svg"), "<!DOCTYPE svg [<!ENTITY data SYSTEM 'file:///invalid'>]><svg/>");
        File.WriteAllBytes(Path.Combine(exact, "exact.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNo/n/iPwAGrgLTbPTK5wAAAABJRU5ErkJggg=="));
        catalog.LoadPack(fr);
        catalog.LoadPack(exact);
        var display = CultureInfo.GetCultureInfo("fr-CA");

        Assert.Equal("Québec", catalog.Text(new("label.exact"), display));
        Assert.Equal("Parent français", catalog.Text(new("label.parent"), display));
        Assert.Equal("Default", catalog.Text(new("label.base"), display));
        Assert.Equal(LocaleCatalog.ImageKind.Png, catalog.Image(new("icon.exact"), display).Kind);
        Assert.Equal(LocaleCatalog.ImageKind.Svg, catalog.Image(new("icon.open"), display).Kind);
        Assert.Equal(LocaleCatalog.ImageKind.Geometry, catalog.Image(new("icon.fallback"), display).Kind);
        Assert.Equal("[label.unknown]", catalog.Text(new("label.unknown"), display));
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr-CA", Slug: "label.parent", Kind: "text", Code: "invalid_resource" });
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr", Slug: "icon.fallback", Kind: "image", Code: "invalid_resource" });
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr-CA", Slug: "icon.open", Kind: "image", Code: "invalid_resource" });
        Assert.Contains(catalog.Diagnostics, issue => issue is { Slug: "label.unknown", Kind: "text", Code: "missing_resource" });
    }

    /// <summary>Translated plural rules use the source locale and number/date styles use the chosen formatting culture.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-localized-formatting; TEST-RETRO-008#ac-test-pack-validation</remarks>
    [Fact]
    public void Named_plural_and_select_formats_preserve_typed_arguments_and_culture()
    {
        using var fixture = new LocaleFixture();
        using var english = new MemoryStream(Encoding.UTF8.GetBytes("""{"locale":"en","version":1}"""));
        var catalog = new LocaleCatalog(english);
        var parent = fixture.Pack("fr", """
            {"locale":"fr","version":1,"text":{"message.summary":{
              "pattern":"{count, plural, one {fichier} other {fichiers}} / {kind, select, rom {ROM} other {data}} / {size, number} / {when, date, short}",
              "arguments":{"count":"number","kind":"text","size":"number","when":"date"}}}}
            """);
        var invalid = fixture.Pack("fr-CA", """
            {"locale":"fr-CA","version":1,"text":{"message.summary":{
              "pattern":"{count, plural, one {{unknown}} other {ok}}","arguments":{"count":"number"}}}}
            """);
        catalog.LoadPack(parent);
        catalog.LoadPack(invalid);
        var display = CultureInfo.GetCultureInfo("fr-CA");
        var format = CultureInfo.GetCultureInfo("de-DE");
        var date = new DateTime(2026, 11, 26);
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["count"] = 0, ["kind"] = "rom", ["size"] = 12.5m, ["when"] = date,
        };

        Assert.Equal($"fichier / ROM / {12.5m.ToString("G", format)} / {date.ToString("d", format)}",
            catalog.Text(new("message.summary"), display, arguments, format));
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr-CA", Slug: "message.summary", Code: "invalid_resource" });

        arguments["size"] = "12.5";
        Assert.Equal("[message.summary]", catalog.Text(new("message.summary"), display, arguments, format));
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr", Slug: "message.summary", Code: "format_failed" });
    }

    /// <summary>Translations must preserve the bundled message's named argument schema.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-pack-validation</remarks>
    [Fact]
    public void Translations_that_change_argument_names_or_types_are_skipped()
    {
        using var fixture = new LocaleFixture();
        using var english = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"locale":"en","version":1,"text":{"message.summary":{
              "pattern":"Files: {count, number}","arguments":{"count":"number"}}}}
            """));
        var catalog = new LocaleCatalog(english);
        var translated = fixture.Pack("fr", """
            {"locale":"fr","version":1,"text":{"message.summary":{
              "pattern":"Fichiers : {count}","arguments":{"count":"text"}}}}
            """);
        catalog.LoadPack(translated);
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal) { ["count"] = 3 };
        Assert.Equal("Files: 3", catalog.Text(new("message.summary"), CultureInfo.GetCultureInfo("fr"), arguments));
        Assert.Contains(catalog.Diagnostics, issue => issue is { Locale: "fr", Slug: "message.summary", Code: "incompatible_arguments" });
    }

    /// <summary>The build validator rejects overlapping gestures for each supported host.</summary>
    [Theory]
    [InlineData(true, true, "default")]
    [InlineData(true, false, "Windows")]
    [InlineData(false, true, "Linux")]
    public void Bundled_command_validation_rejects_overlapping_gestures(bool windows, bool linux, string platform)
    {
        using var english = new WorkbenchLocalization(initial: CultureInfo.GetCultureInfo("en"));
        var baseline = WorkbenchCommands.FocusTool;
        var conflict = WorkbenchCommands.OpenFolder with
        {
            DefaultGesture = windows && linux ? baseline.DefaultGesture : WorkbenchCommands.OpenFolder.DefaultGesture,
            WindowsGesture = windows ? baseline.DefaultGesture : null,
            LinuxGesture = linux ? baseline.DefaultGesture : null,
        };
        var rejected = Assert.Throws<InvalidDataException>(() => WorkbenchCommands.Validate([baseline, conflict], english));
        Assert.Contains($"share {platform} gesture", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>The build validator requires unique IDs, valid gestures, and both default resource kinds.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-pack-validation</remarks>
    [Fact]
    public void Bundled_command_validation_checks_metadata_and_requires_the_correct_resource_kinds()
    {
        WorkbenchCommands.ValidateBundledResources();
        using var english = new WorkbenchLocalization(initial: CultureInfo.GetCultureInfo("en"));
        var baseline = WorkbenchCommands.FocusTool;
        Assert.Throws<InvalidDataException>(() => WorkbenchCommands.Validate([baseline, baseline], english));
        Assert.Throws<InvalidDataException>(() =>
            WorkbenchCommands.Validate([baseline with { DefaultGesture = "Ctrl+NotARealKey" }], english));
        var error = Assert.Throws<InvalidDataException>(() => WorkbenchCommands.Validate(
            [baseline with { LabelSlug = baseline.IconSlug, IconSlug = baseline.LabelSlug }], english));
        Assert.Contains($"text '{baseline.IconSlug}' (missing_resource)", error.Message, StringComparison.Ordinal);
        Assert.Contains($"image '{baseline.LabelSlug}' (missing_resource)", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Language changes update rendered text, accessible names, images, and docked and floating layout direction.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-live-language</remarks>
    [AvaloniaFact]
    public async Task Language_change_refreshes_open_surfaces_and_persists_after_reopen()
    {
        using var fixture = new LocaleFixture();
        var arabic = fixture.Pack("ar", """
            {"locale":"ar","version":1,"text":{"command.workbench.view.focus.label":"تركيز النافذة",
              "command.workspace.open-folder.label":"افتح مجلدًا","pane.explorer.title":"المستكشف"}, "images":{"command.workbench.view.focus.icon":"icons/open.svg"}}
            """);
        WriteIcon(arabic);
        fixture.Pack("qps-ploc", """
            {"locale":"qps-ploc","version":1,"text":{"command.workbench.view.focus.label":"[!! Fóócús tóól pááné fór lóngér tééstíng !!]"}}
            """);
        using var session = new WorkbenchSession(new WorkspaceExplorerStore(Path.Combine(fixture.Path, "workspace.json")), null,
            new WorkbenchLocalization(fixture.Path, CultureInfo.GetCultureInfo("en")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var workbenchCommand = window.FocusButton.Command;
            var shortcut = window.FocusShortcut.Gesture;
            var tool = session.Layout.Tool("explorer");
            session.Layout.Factory.FloatDockable(tool);
            var floating = Assert.IsAssignableFrom<Window>(Assert.Single(session.Layout.Root.Windows!).Host);
            var button = await WaitForFolderButtonAsync(floating, timeout.Token);
            await ChooseLanguageAsync(window, session, "ar");
            Assert.Equal("تركيز النافذة", window.FocusButtonLabel.Text);
            Assert.Equal("تركيز النافذة", AutomationProperties.GetName(window.FocusButton));
            Assert.Equal("افتح مجلدًا", AutomationProperties.GetName(button));
            Assert.Equal("المستكشف", tool.Title);
            Assert.IsType<Image>(window.FocusMenu.Icon);
            Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, floating.FlowDirection);
            Assert.Same(workbenchCommand, window.FocusButton.Command);
            Assert.Equal(shortcut, window.FocusShortcut.Gesture);

            await session.View.SetDisplayLanguageCommand.ExecuteAsync("qps-ploc");
            Assert.StartsWith("[!!", window.FocusButtonLabel.Text, StringComparison.Ordinal);
            Assert.Equal(FlowDirection.LeftToRight, floating.FlowDirection);
            using var reopened = new WorkbenchLocalization(fixture.Path, CultureInfo.GetCultureInfo("en"));
            Assert.Equal("qps-ploc", reopened.DisplayCulture.Name, ignoreCase: true);
            Assert.Equal("Open Folder…", reopened.Text("command.workspace.open-folder.label"));
        }
        finally { window.Close(); }
    }

    /// <summary>Live Explorer rows localize status and kind while preserving the selected source and its name.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-live-language; TEST-RETRO-008#ac-test-resource-fallback</remarks>
    [AvaloniaFact]
    public async Task Language_change_refreshes_explorer_rows_without_changing_the_tree()
    {
        using var fixture = new LocaleFixture();
        fixture.Pack("fr", """
            {"version":1,"locale":"fr","text":{
              "explorer.root.folder":{"pattern":"Dossier : {path}","arguments":{"path":"text"}},
              "explorer.root.unavailable":{"pattern":"{root} (indisponible)","arguments":{"root":"text"}}
            }}
            """);
        fixture.Pack("fr-CA", """
            {"version":1,"locale":"fr-CA","text":{"explorer.branch.unavailable":"Répertoire introuvable"}}
            """);
        var folder = Directory.CreateDirectory(Path.Combine(fixture.Path, "folder")).FullName;
        var project = Directory.CreateDirectory(Path.Combine(fixture.Path, "project")).FullName;
        const string name = "même {source}.asm";
        File.WriteAllText(Path.Combine(folder, name), "move d0,d1");
        using var session = new WorkbenchSession(new WorkspaceExplorerStore(Path.Combine(fixture.Path, "workspace.json")), null,
            new WorkbenchLocalization(fixture.Path, CultureInfo.GetCultureInfo("en")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            Assert.True(session.Layout.Focus("explorer"));
            var explorer = session.ExplorerView;
            await explorer.OpenFolderCommand.ExecuteAsync(folder);
            await explorer.OpenProjectCommand.ExecuteAsync(project);
            var opened = explorer.TreeRoots[0];
            var missing = explorer.TreeRoots[1];
            Assert.Equal("Loading…", Assert.Single(opened.Children).Label);
            await explorer.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(opened, true));
            while (opened.Children is not [{ Address: not null }])
                await Task.Delay(25, token);
            var file = Assert.Single(opened.Children);
            await explorer.SelectEntryCommand.ExecuteAsync(file);
            Directory.Delete(project);
            await explorer.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(missing, true));
            while (!missing.Label.EndsWith("(unavailable)", StringComparison.Ordinal))
                await Task.Delay(25, token);

            await ChooseLanguageAsync(window, session, "fr-CA");
            Assert.Equal($"Dossier : {folder}", opened.Label);
            Assert.Equal($"Project: {project} (indisponible)", missing.Label);
            Assert.Equal("Répertoire introuvable", Assert.Single(missing.Children).Label);
            Assert.Equal(name, file.Label);
            Assert.Same(opened, explorer.TreeRoots[0]);
            Assert.Same(file, explorer.SelectedNode);
            Assert.True(opened.IsExpanded);

            var rootItem = window.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(item => ReferenceEquals(item.DataContext, opened));
            Assert.Equal(opened.Label, AutomationProperties.GetName(rootItem));
            Assert.Contains(rootItem.GetVisualDescendants().OfType<TextBlock>(),
                label => string.Equals(label.Text, opened.Label, StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>A local language choice survives a Dock revision change while waiting for the dispatcher.</summary>
    /// <remarks>TEST-RETRO-008#ac-test-live-language</remarks>
    [AvaloniaFact]
    public async Task Local_language_selection_survives_a_queued_dock_revision_change()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        await session.View.SetDisplayLanguageCommand.ExecuteAsync("en");
        var initial = session.View.WorkbenchRevision;
        var pending = session.View.SetDisplayLanguageCommand.ExecuteAsync("ar");
        session.Layout.AdvanceWorkspaceRevision();
        await pending;

        Assert.Equal("ok", session.View.LastResult?.Code);
        Assert.Equal("ar", session.Localization.DisplayCulture.Name);
        Assert.Equal(initial + 2, session.View.WorkbenchRevision);
    }

    /// <summary>The MCP write uses the same scoped language setting and rejects a stale workbench revision.</summary>
    [AvaloniaFact]
    public async Task Mcp_changes_display_language_using_the_live_workbench_context()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
        var descriptor = host.CreateAttachment("locale-test", canMutate: true);
        using var http = descriptor.CreateHttpClient();
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = descriptor.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http), cancellationToken: token);

        var context = session.View.Address;
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["locale"] = "ar", ["context"] = context };
        var changed = await client.CallToolAsync("workbench_set_display_language", args, cancellationToken: token);
        Assert.Equal("ok", changed.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Equal("ar", session.Localization.DisplayCulture.Name);
        var live = await client.CallToolAsync("workbench_get_display_language", cancellationToken: token);
        Assert.True(live.StructuredContent!.Value.GetProperty("data").GetProperty("rightToLeft").GetBoolean());

        args["locale"] = "en";
        var stale = await client.CallToolAsync("workbench_set_display_language", args, cancellationToken: token);
        Assert.Equal("stale_revision", stale.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Equal("ar", session.Localization.DisplayCulture.Name);
    }

    private static async Task ChooseLanguageAsync(WorkbenchWindow window, WorkbenchSession session, string tag)
    {
        var viewMenu = window.GetVisualDescendants().OfType<MenuItem>().Single(menu => Equals(menu.Header, "_View"));
        viewMenu.IsSubMenuOpen = true;
        window.UpdateLayout();
        var languageMenu = window.GetVisualDescendants().OfType<MenuItem>().Single(menu => Equals(menu.Header, "Display Language"));
        languageMenu.IsSubMenuOpen = true;
        await WorkbenchRemoteControl.ClickAsync(window, "workbench.language." + tag);
        await Assert.IsAssignableFrom<Task>(session.View.SetDisplayLanguageCommand.ExecutionTask);
        Assert.Equal("ok", session.View.LastResult?.Code);
    }

    private static async Task<Button> WaitForFolderButtonAsync(Window window, CancellationToken token)
    {
        while (true)
        {
            window.UpdateLayout();
            var result = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                string.Equals(AutomationProperties.GetAutomationId(button), "workspace.open-folder.toolbar", StringComparison.Ordinal));
            if (result is not null)
                return result;
            await Task.Delay(25, token);
        }
    }

    private static void WriteIcon(string directory)
    {
        var icons = Directory.CreateDirectory(Path.Combine(directory, "icons"));
        File.WriteAllText(Path.Combine(icons.FullName, "open.svg"), """
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 18 18"><rect width="18" height="18" fill="#835fee"/></svg>
            """);
    }

    private sealed class LocaleFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("retro-language-");
        public string Path => _directory.FullName;

        public string Pack(string name, string manifest)
        {
            var folder = Directory.CreateDirectory(System.IO.Path.Combine(Path, "locales", name)).FullName;
            File.WriteAllText(System.IO.Path.Combine(folder, "manifest.json"), manifest);
            return folder;
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
