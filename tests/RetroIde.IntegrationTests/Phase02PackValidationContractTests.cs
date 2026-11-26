using System.Text;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Defines rejection and bundled coverage checks for the workbench language pack contract.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-008#ac-test-pack-validation. Reject invalid entries while preserving the valid bundled defaults.</remarks>
public sealed partial class Phase02PackValidationContractTests
{
    private static readonly string[] CommandIds =
    [
        "workbench.view.focus", "workspace.open-project", "workspace.open-folder", "document.save", "document.reveal",
        "document.search", "document.next", "document.previous", "document.replace", "document.replace-all",
        "document.close", "document.discard", "document.undo", "document.redo",
    ];

    private static readonly string[] PaneIds =
    [
        "explorer", "registers", "call-stack", "output", "problems", "serial", "breakpoints", "mcp-activity",
        "start", "emulation", "disassembly", "test-results",
    ];

    private static readonly string[] RequiredText =
    [
        "menu.file", "menu.view", "menu.language", "editor.search.query", "editor.search.replacement",
        "editor.search.match-case", "explorer.branch.loading", "explorer.branch.unavailable", "explorer.root.folder",
        "explorer.root.project", "explorer.root.unavailable", "toolbar.focus.selector", "status.no-emulator",
        .. CommandIds.Select(id => $"command.{id}.label"),
        .. PaneIds.SelectMany(id => new[] { $"pane.{id}.title", $"pane.{id}.description" }),
    ];

    private static readonly string[] RequiredIcons = [.. CommandIds.Select(id => $"command.{id}.icon")];

    /// <summary>Demonstrates that valid fallback and full bundled coverage pass and each omitted check is detectable.</summary>
    [Theory]
    [InlineData("accept-invalid-pattern")]
    [InlineData("accept-incompatible-arguments")]
    [InlineData("accept-wrong-type")]
    [InlineData("accept-path-escape")]
    [InlineData("missing-default-pane")]
    [InlineData("missing-default-icon")]
    public void Reference_rejects_invalid_packs_and_incomplete_bundled_coverage(string fault)
    {
        using var fixture = new ValidationFiles();
        VerifyPack(new ReferenceValidation(), fixture);
        Assert.ThrowsAny<XunitException>(() => VerifyPack(new ReferenceValidation(fault), fixture));
    }

    private static void VerifyPack(IValidationPort port, ValidationFiles fixture)
    {
        var external = port.CheckPack(fixture);
        Assert.Contains(new Issue("fr", "text", "label.pattern", "invalid_resource"), external.Diagnostics);
        Assert.Contains(new Issue("fr", "text", "label.arguments", "incompatible_arguments"), external.Diagnostics);
        Assert.Contains(new Issue("fr", "text", "label.type", "invalid_resource"), external.Diagnostics);
        Assert.Contains(new Issue("fr", "image", "icon.escape", "invalid_resource"), external.Diagnostics);
        Assert.Equal("Pattern default", external.Pattern);
        Assert.Equal("Default 2", external.Arguments);
        Assert.Equal("Text default", external.Type);
        Assert.Equal("Geometry", external.EscapedImage);

        var bundled = port.CheckBundledResources();
        Assert.All(RequiredText, slug => Assert.Contains(slug, bundled.Text));
        Assert.All(RequiredIcons, slug => Assert.Contains(slug, bundled.Icons));
    }

    private interface IValidationPort
    {
        PackResult CheckPack(ValidationFiles fixture);
        BundleResult CheckBundledResources();
    }

    private sealed record Issue(string Locale, string Kind, string Slug, string Code);
    private sealed record PackResult(string Pattern, string Arguments, string Type, string EscapedImage, IReadOnlyList<Issue> Diagnostics);
    private sealed record BundleResult(IReadOnlySet<string> Text, IReadOnlySet<string> Icons);

    private sealed class ValidationFiles : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-pack-validation-contract-");

        public ValidationFiles()
        {
            // The escaped path points to a real valid icon. Validation must reject its location.
            File.WriteAllText(Path.Combine(_root.FullName, "outside.svg"),
                """<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8"/></svg>""");
            Pack = _root.CreateSubdirectory("fr").FullName;
            File.WriteAllText(Path.Combine(Pack, "manifest.json"), """
                {"locale":"fr","version":1,"text":{
                  "label.pattern":{"pattern":"{count, plural, one {{unknown}} other {ok}}","arguments":{"count":"number"}},
                  "label.arguments":{"pattern":"Traduction {count}","arguments":{"count":"text"}},
                  "label.type":17},
                 "images":{"icon.escape":"../outside.svg"}}
                """);
        }

        public string Pack { get; }

        public static Stream English() => new MemoryStream(Encoding.UTF8.GetBytes("""
            {"locale":"en","version":1,"text":{"label.pattern":"Pattern default","label.type":"Text default",
               "label.arguments":{"pattern":"Default {count, number}","arguments":{"count":"number"}}},
               "icons":{"icon.escape":"M0,0 L6,0 L0,6 Z"}}
            """));

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferenceValidation(string? fault = null) : IValidationPort
    {
        public PackResult CheckPack(ValidationFiles fixture)
        {
            Assert.True(File.Exists(Path.Combine(fixture.Pack, "..", "outside.svg")));
            var issues = new List<Issue>();
            if (!Fault("accept-invalid-pattern"))
                issues.Add(new Issue("fr", "text", "label.pattern", "invalid_resource"));
            if (!Fault("accept-incompatible-arguments"))
                issues.Add(new Issue("fr", "text", "label.arguments", "incompatible_arguments"));
            if (!Fault("accept-wrong-type"))
                issues.Add(new Issue("fr", "text", "label.type", "invalid_resource"));
            if (!Fault("accept-path-escape"))
                issues.Add(new Issue("fr", "image", "icon.escape", "invalid_resource"));
            return new PackResult("Pattern default", "Default 2", "Text default",
                Fault("accept-path-escape") ? "Svg" : "Geometry", issues);
        }

        public BundleResult CheckBundledResources()
        {
            var text = RequiredText.ToHashSet(StringComparer.Ordinal);
            var icons = RequiredIcons.ToHashSet(StringComparer.Ordinal);
            if (Fault("missing-default-pane"))
                text.Remove("pane.serial.title");
            if (Fault("missing-default-icon"))
                icons.Remove("command.document.save.icon");
            return new BundleResult(text, icons);
        }

        private bool Fault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
