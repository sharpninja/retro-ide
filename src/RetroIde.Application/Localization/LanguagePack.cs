using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetroIde.Application.Localization;

internal sealed partial class LanguagePack
{
    private LanguagePack(CultureInfo culture) => Culture = culture;

    public CultureInfo Culture { get; }
    public string Locale => Culture.Name;
    public Dictionary<string, LocalizedMessage> Text { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, LocaleCatalog.ImageResource> Images { get; } = new(StringComparer.Ordinal);

    public static LanguagePack Load(Stream stream, string? directory, Action<LocaleCatalog.Diagnostic> report)
    {
        using (stream)
        using (var document = JsonDocument.Parse(stream))
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1 ||
                !root.TryGetProperty("locale", out var locale) || locale.ValueKind != JsonValueKind.String)
                throw new FormatException("A language pack requires a locale and version 1.");

            var pack = new LanguagePack(ParseCulture(locale.GetString()!));
            pack.ReadEntries(root, "text", "text", pack.Text, LocalizedMessage.Load, report);
            if (directory is null)
                pack.ReadEntries(root, "icons", "image", pack.Images, LoadBundledGeometry, report);
            else
                pack.ReadEntries(root, "images", "image", pack.Images, entry => PackImageLoader.Load(directory, entry), report);
            return pack;
        }
    }

    public static CultureInfo ParseCulture(string tag)
    {
        if (!LanguageTag().IsMatch(tag))
            throw new FormatException("The display language must use a BCP 47 language tag.");
        try { return CultureInfo.GetCultureInfo(tag); }
        catch (CultureNotFoundException error) { throw new FormatException("The locale is not supported by the runtime.", error); }
    }

    internal static bool IsResourceSlug(string value) => Slug().IsMatch(value);

    private void ReadEntries<T>(JsonElement root, string property, string kind, Dictionary<string, T> entries,
        Func<JsonElement, T> load, Action<LocaleCatalog.Diagnostic> report)
    {
        if (!root.TryGetProperty(property, out var input))
            return;
        if (input.ValueKind != JsonValueKind.Object)
        {
            report(new LocaleCatalog.Diagnostic(Locale, kind, "", "invalid_entries"));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in input.EnumerateObject())
        {
            if (!IsResourceSlug(entry.Name) || !seen.Add(entry.Name))
            {
                entries.Remove(entry.Name);
                report(new LocaleCatalog.Diagnostic(Locale, kind, entry.Name, "invalid_slug"));
                continue;
            }
            try { entries.Add(entry.Name, load(entry.Value)); }
            catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or System.Xml.XmlException)
            {
                report(new LocaleCatalog.Diagnostic(Locale, kind, entry.Name, "invalid_resource"));
            }
        }
    }

    private static LocaleCatalog.ImageResource LoadBundledGeometry(JsonElement entry) =>
        new(LocaleCatalog.ImageKind.Geometry, [], entry.GetString() ?? throw new FormatException("Missing geometry."));

    [GeneratedRegex(@"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LanguageTag();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Slug();
}
