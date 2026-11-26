using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using RetroIde.Application;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02PackValidationContractTests
{
    /// <summary>Binds pack inputs to the installed catalog and checks the entire compiled English bundle.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-008#ac-test-pack-validation. Run the same assertions on either runtime build.</remarks>
    [Fact]
    public void Workbench_rejects_invalid_entries_and_validates_the_default_bundle()
    {
        var type = typeof(WorkbenchAddress).Assembly.GetType("RetroIde.Application.Localization.LocaleCatalog");
        var workbench = typeof(WorkbenchSession).Assembly;
        var commands = workbench.GetType("RetroIde.Workbench.WorkbenchCommands");
        var validate = commands?.GetMethod("ValidateBundledResources", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(type is not null && validate is not null,
            "The live workbench did not expose pack and default-resource validation.");
        using var fixture = new ValidationFiles();
        VerifyPack(new LiveValidation(type, workbench, validate), fixture);
    }

    private sealed class LiveValidation(Type catalogType, Assembly workbench, MethodInfo validateBundle) : IValidationPort
    {
        public PackResult CheckPack(ValidationFiles fixture)
        {
            using var english = ValidationFiles.English();
            var catalog = Assert.IsAssignableFrom<object>(Activator.CreateInstance(catalogType, english));
            Method("LoadPack").Invoke(catalog, [fixture.Pack]);
            var culture = CultureInfo.GetCultureInfo("fr");
            var formatting = CultureInfo.GetCultureInfo("en");
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal) { ["count"] = 2 };
            var pattern = Text("label.pattern", null);
            var localizedArgs = Text("label.arguments", arguments);
            var type = Text("label.type", null);
            var image = Assert.IsAssignableFrom<object>(Method("Image").Invoke(catalog, [Slug("ImageSlug", "icon.escape"), culture, null]));
            var imageKind = Assert.IsAssignableFrom<Enum>(Read(image, "Kind")).ToString();
            var diagnostics = Assert.IsAssignableFrom<IEnumerable>(Read(catalog, "Diagnostics")).Cast<object>()
                .Select(issue => new Issue(ReadString(issue, "Locale"), ReadString(issue, "Kind"), ReadString(issue, "Slug"), ReadString(issue, "Code")))
                .ToArray();
            return new PackResult(pattern, localizedArgs, type, imageKind, diagnostics);

            string Text(string slug, IReadOnlyDictionary<string, object>? args) =>
                Assert.IsType<string>(Method("Text").Invoke(catalog, [Slug("TextSlug", slug), culture, args, formatting]));
        }

        public BundleResult CheckBundledResources()
        {
            // This is the same validator invoked by the workbench's build target.
            validateBundle.Invoke(null, null);
            using var english = workbench.GetManifestResourceStream("RetroIde.Workbench.Resources.en.json");
            Assert.NotNull(english);
            using var manifest = JsonDocument.Parse(english);
            var root = manifest.RootElement;
            Assert.Equal("en", root.GetProperty("locale").GetString());
            var text = root.GetProperty("text").EnumerateObject()
                .Where(entry => entry.Value.ValueKind == JsonValueKind.Object || HasValue(entry.Value))
                .Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
            var icons = root.GetProperty("icons").EnumerateObject().Where(entry => HasValue(entry.Value))
                .Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
            return new BundleResult(text, icons);
        }

        private MethodInfo Method(string name) => Assert.IsAssignableFrom<MethodInfo>(catalogType.GetMethod(name));

        private object Slug(string type, string key) =>
            Assert.IsAssignableFrom<object>(Activator.CreateInstance(
                Assert.IsAssignableFrom<Type>(catalogType.GetNestedType(type, BindingFlags.NonPublic)), key));

        private static bool HasValue(JsonElement value) =>
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());

        private static string ReadString(object instance, string member) => Assert.IsType<string>(Read(instance, member));

        private static object? Read(object instance, string member) =>
            Assert.IsAssignableFrom<PropertyInfo>(instance.GetType().GetProperty(member)).GetValue(instance);
    }
}
