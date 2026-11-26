using System.Collections.Concurrent;
using System.Globalization;

namespace RetroIde.Application.Localization;

internal sealed class LocaleCatalog
{
    internal readonly record struct TextSlug(string Value);
    internal readonly record struct ImageSlug(string Value);
    internal enum ImageKind { Geometry, Svg, Png }
    internal sealed record ImageResource(ImageKind Kind, byte[] Bytes, string? Geometry = null);
    internal sealed record Diagnostic(string Locale, string Kind, string Slug, string Code);

    private static readonly ImageResource MissingIcon = new(ImageKind.Geometry, [], "M2,2 L18,2 L18,18 L2,18 Z");
    private readonly Dictionary<string, LanguagePack> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Diagnostic, byte> _diagnostics = new();

    public LocaleCatalog(Stream english)
    {
        var fallback = LanguagePack.Load(english, directory: null, Report);
        if (!string.Equals(fallback.Locale, "en", StringComparison.Ordinal))
            throw new InvalidDataException("The bundled catalog must supply English.");
        _languages.Add(fallback.Locale, fallback);
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Keys.ToArray();

    public IReadOnlyList<string> Languages => _languages.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void LoadPack(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory), "manifest.json");
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new FormatException("A language-pack catalog must be stored inside its pack.");
            var catalog = LanguagePack.Load(File.OpenRead(path), Path.GetDirectoryName(path), Report);
            if (_languages.ContainsKey(catalog.Locale))
            {
                Report(new Diagnostic(catalog.Locale, "pack", "", "duplicate_locale"));
                return;
            }

            var defaults = _languages["en"];
            foreach (var entry in catalog.Text.ToArray())
            {
                if (defaults.Text.TryGetValue(entry.Key, out var source) && !entry.Value.HasSameArguments(source))
                {
                    catalog.Text.Remove(entry.Key);
                    Report(new Diagnostic(catalog.Locale, "text", entry.Key, "incompatible_arguments"));
                }
            }
            _languages.Add(catalog.Locale, catalog);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            Report(new Diagnostic(Path.GetFileName(directory), "pack", "", "invalid_pack"));
        }
    }

    public string Text(TextSlug slug, CultureInfo display, IReadOnlyDictionary<string, object>? arguments = null,
        CultureInfo? formatting = null)
    {
        foreach (var pack in Lookup(display.Name))
        {
            if (pack.Text.TryGetValue(slug.Value, out var message))
            {
                try { return message.Format(pack.Culture, formatting ?? display, arguments); }
                catch (FormatException) { Report(new Diagnostic(pack.Locale, "text", slug.Value, "format_failed")); }
            }
        }
        Report(new Diagnostic(display.Name, "text", slug.Value, "missing_resource"));
        return $"[{slug.Value}]";
    }

    public ImageResource Image(ImageSlug slug, CultureInfo display, Func<ImageResource, bool>? accepts = null)
    {
        foreach (var pack in Lookup(display.Name))
        {
            if (pack.Images.TryGetValue(slug.Value, out var image))
            {
                if (accepts?.Invoke(image) != false)
                    return image;
                Report(new Diagnostic(pack.Locale, "image", slug.Value, "decode_failed"));
            }
        }
        Report(new Diagnostic(display.Name, "image", slug.Value, "missing_resource"));
        return MissingIcon;
    }

    private IEnumerable<LanguagePack> Lookup(string tag)
    {
        var locale = tag;
        while (locale.Length > 0)
        {
            if (_languages.TryGetValue(locale, out var pack))
                yield return pack;
            if (string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase))
                yield break;
            var boundary = locale.LastIndexOf('-');
            if (boundary < 0)
                break;
            locale = locale[..boundary];
            boundary = locale.LastIndexOf('-');
            if (boundary >= 0 && locale.Length - boundary == 2)
                locale = locale[..boundary];
        }
        yield return _languages["en"];
    }

    private void Report(Diagnostic diagnostic) => _diagnostics.TryAdd(diagnostic, 0);
}
