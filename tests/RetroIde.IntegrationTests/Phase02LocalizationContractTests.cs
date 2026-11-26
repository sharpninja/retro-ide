using System.Globalization;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Defines typed resource and formatting assertions shared by the reference and workbench catalogs.</summary>
/// <remarks>
/// 2026-11-26 · TEST-RETRO-008#ac-test-resource-fallback, TEST-RETRO-008#ac-test-pack-validation,
/// TEST-RETRO-008#ac-test-localized-formatting. Verify that each controlled resource fault is observable.
/// </remarks>
public sealed partial class Phase02LocalizationContractTests
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("fr-CA");
    private static readonly CultureInfo FormattingCulture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Checks that favorable fixtures pass and deliberately broken fallback or validation fails.</summary>
    [Theory]
    [InlineData("no-parent-text")]
    [InlineData("no-parent-image")]
    [InlineData("skip-english")]
    [InlineData("invalid-entry-unreported")]
    [InlineData("wrong-formatting-culture")]
    public void Scripted_resources_distinguish_fallback_and_validation_faults(string fault)
    {
        VerifyResources(new ReferenceCatalog());
        Assert.ThrowsAny<XunitException>(() => VerifyResources(new ReferenceCatalog(fault)));
    }

    private static void VerifyResources(ICatalog catalog)
    {
        Assert.Equal("Réglages du Québec", catalog.Text("label.exact"));
        Assert.Equal("Projet", catalog.Text("label.project"));
        Assert.Equal("Output", catalog.Text("label.output"));
        Assert.Equal("Png", catalog.ImageKind("icon.exact"));
        Assert.Equal("Svg", catalog.ImageKind("icon.open"));
        Assert.Equal("Geometry", catalog.ImageKind("icon.output"));
        Assert.Contains(new ResourceDiagnostic("fr-CA", "text", "label.project", "invalid_resource"), catalog.Diagnostics);
        Assert.Contains(new ResourceDiagnostic("fr-CA", "image", "icon.open", "invalid_resource"), catalog.Diagnostics);
        Assert.Contains(new ResourceDiagnostic("fr", "text", "label.invalid", "incompatible_arguments"), catalog.Diagnostics);

        var date = new DateTime(2026, 11, 26);
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["count"] = 0, ["type"] = "rom", ["size"] = 12.5m, ["date"] = date,
        };
        Assert.Equal($"fichier / ROM / {12.5m.ToString("G", FormattingCulture)} / {date.ToString("d", FormattingCulture)}",
            catalog.Text("message.summary", arguments));
    }

    private interface ICatalog
    {
        string Text(string slug, IReadOnlyDictionary<string, object>? arguments = null);
        string ImageKind(string slug);
        IReadOnlyList<ResourceDiagnostic> Diagnostics { get; }
    }

    private sealed record ResourceDiagnostic(string Locale, string Kind, string Slug, string Code);

    private sealed class ReferenceCatalog(string? fault = null) : ICatalog
    {
        public string Text(string slug, IReadOnlyDictionary<string, object>? arguments = null) => slug switch
        {
            "label.exact" => "Réglages du Québec",
            "label.project" => Fails("no-parent-text") ? "Project" : "Projet",
            "label.output" => Fails("skip-english") ? "[label.output]" : "Output",
            "message.summary" => Format(arguments),
            _ => throw new ArgumentOutOfRangeException(nameof(slug)),
        };

        public string ImageKind(string slug) => slug switch
        {
            "icon.exact" => "Png",
            "icon.open" => Fails("no-parent-image") ? "Geometry" : "Svg",
            "icon.output" => "Geometry",
            _ => throw new ArgumentOutOfRangeException(nameof(slug)),
        };

        public IReadOnlyList<ResourceDiagnostic> Diagnostics => Fails("invalid-entry-unreported")
            ? []
            :
            [
                new("fr-CA", "text", "label.project", "invalid_resource"),
                new("fr-CA", "image", "icon.open", "invalid_resource"),
                new("fr", "text", "label.invalid", "incompatible_arguments"),
            ];

        private string Format(IReadOnlyDictionary<string, object>? arguments)
        {
            Assert.NotNull(arguments);
            Assert.Equal(0, Assert.IsType<int>(arguments["count"]));
            Assert.Equal("rom", Assert.IsType<string>(arguments["type"]));
            var culture = Fails("wrong-formatting-culture") ? DisplayCulture : FormattingCulture;
            var size = Assert.IsType<decimal>(arguments["size"]);
            var date = Assert.IsType<DateTime>(arguments["date"]);
            return $"fichier / ROM / {size.ToString("G", culture)} / {date.ToString("d", culture)}";
        }

        private bool Fails(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }

    private sealed class PackFixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-resource-contract-");

        public PackFixture()
        {
            Parent = WritePack("fr", """
                {"locale":"fr","version":1,"text":{
                  "label.project":"Projet",
                  "label.invalid":{"pattern":"Invalid {count}","arguments":{"count":"text"}},
                  "message.summary":{"pattern":"{count, plural, one {fichier} other {fichiers}} / {type, select, rom {ROM} other {data}} / {size, number} / {date, date, short}",
                    "arguments":{"count":"number","type":"text","size":"number","date":"date"}}},
                  "images":{"icon.open":"open.svg"}}
                """);
            File.WriteAllText(Path.Combine(Parent, "open.svg"),
                """<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8"/></svg>""");

            Exact = WritePack("fr-CA", """
                {"locale":"fr-CA","version":1,"text":{"label.exact":"Réglages du Québec","label.project":4},
                  "images":{"icon.exact":"exact.png","icon.open":"missing.svg"}}
                """);
            File.WriteAllBytes(Path.Combine(Exact, "exact.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNo/n/iPwAGrgLTbPTK5wAAAABJRU5ErkJggg=="));
        }

        public string Parent { get; }
        public string Exact { get; }

        public static Stream English() => new MemoryStream(Encoding.UTF8.GetBytes("""
            {"locale":"en","version":1,"text":{"label.project":"Project","label.output":"Output",
              "label.invalid":{"pattern":"Default {count, number}","arguments":{"count":"number"}}},
              "icons":{"icon.open":"M0,0 L6,0 L0,6 Z","icon.output":"M0,0 L8,8 L0,8 Z"}}
            """));

        public void Dispose() => _root.Delete(recursive: true);

        private string WritePack(string name, string manifest)
        {
            var pack = _root.CreateSubdirectory(name);
            File.WriteAllText(Path.Combine(pack.FullName, "manifest.json"), manifest);
            return pack.FullName;
        }
    }
}
