using System.Collections;
using System.Reflection;
using RetroIde.Application;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02LocalizationContractTests
{
    /// <summary>Exercises the application catalog using persisted language packs and the shared assertions.</summary>
    /// <remarks>
    /// 2026-11-26 · TEST-RETRO-008#ac-test-resource-fallback, TEST-RETRO-008#ac-test-pack-validation,
    /// TEST-RETRO-008#ac-test-localized-formatting. Load the candidate API from the actual tested runtime assembly.
    /// </remarks>
    [Fact]
    public void Workbench_catalog_applies_the_typed_resource_contract()
    {
        var catalogType = typeof(WorkbenchAddress).Assembly.GetType("RetroIde.Application.Localization.LocaleCatalog");
        Assert.True(catalogType is not null, "The workbench application did not expose the typed locale catalog.");
        using var fixture = new PackFixture();
        using var english = PackFixture.English();
        var catalog = new ApplicationCatalog(catalogType, english);
        catalog.Load(fixture.Parent);
        catalog.Load(fixture.Exact);
        VerifyResources(catalog);
    }

    // The discovery boundary keeps the same test runnable on the pre-feature assembly.
    private sealed class ApplicationCatalog(Type type, Stream english) : ICatalog
    {
        private readonly object _catalog = Assert.IsAssignableFrom<object>(Activator.CreateInstance(type, english));

        public IReadOnlyList<ResourceDiagnostic> Diagnostics
        {
            get
            {
                var values = Assert.IsAssignableFrom<IEnumerable>(Property(_catalog, "Diagnostics"));
                return values.Cast<object>()
                    .Select(value => new ResourceDiagnostic(StringProperty(value, "Locale"), StringProperty(value, "Kind"),
                        StringProperty(value, "Slug"), StringProperty(value, "Code")))
                    .ToArray();
            }
        }

        public void Load(string directory) => Method("LoadPack").Invoke(_catalog, [directory]);

        public string Text(string slug, IReadOnlyDictionary<string, object>? arguments = null) =>
            Assert.IsType<string>(Method("Text").Invoke(_catalog, [MakeSlug("TextSlug", slug), DisplayCulture, arguments, FormattingCulture]));

        public string ImageKind(string slug)
        {
            var image = Assert.IsAssignableFrom<object>(Method("Image").Invoke(_catalog, [MakeSlug("ImageSlug", slug), DisplayCulture, null]));
            return Assert.IsAssignableFrom<Enum>(Property(image, "Kind")).ToString();
        }

        private MethodInfo Method(string name) => Assert.IsAssignableFrom<MethodInfo>(type.GetMethod(name));

        private object MakeSlug(string name, string value)
        {
            var definition = Assert.IsAssignableFrom<Type>(type.GetNestedType(name, BindingFlags.NonPublic));
            return Assert.IsAssignableFrom<object>(Activator.CreateInstance(definition, value));
        }

        private static string StringProperty(object target, string name) => Assert.IsType<string>(Property(target, name));

        private static object? Property(object target, string name) =>
            Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(name)).GetValue(target);
    }
}
