using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;
using RetroIde.Application;
using RetroIde.Application.Localization;

namespace RetroIde.Workbench;

internal sealed class WorkbenchLocalization : IDisposable
{
    internal static WorkbenchLocalization English { get; } = new(initial: CultureInfo.GetCultureInfo("en"));
    private readonly Dictionary<LocaleCatalog.ImageResource, Bitmap> _images = new(ReferenceEqualityComparer.Instance);
    private readonly LocaleCatalog _catalog;
    private readonly string? _settings;

    public WorkbenchLocalization(string? folder = null, CultureInfo? initial = null)
    {
        using var english = typeof(WorkbenchLocalization).Assembly.GetManifestResourceStream("RetroIde.Workbench.Resources.en.json")
            ?? throw new InvalidOperationException("The bundled English resource catalog is missing.");
        _catalog = new LocaleCatalog(english);
        FormattingCulture = CultureInfo.CurrentCulture;
        if (folder is not null)
        {
            _settings = Path.Combine(folder, "language.txt");
            var packs = Path.Combine(folder, "locales");
            if (Directory.Exists(packs))
                foreach (var pack in Directory.EnumerateDirectories(packs).Order(StringComparer.Ordinal))
                    _catalog.LoadPack(pack);
        }
        DisplayCulture = LoadPreference() ?? initial ?? CultureInfo.CurrentUICulture;
        if (DisplayCulture.Name.Length == 0)
            DisplayCulture = CultureInfo.GetCultureInfo("en");
    }

    public CultureInfo DisplayCulture { get; private set; }
    public CultureInfo FormattingCulture { get; }
    internal IReadOnlyList<LocaleCatalog.Diagnostic> Diagnostics => _catalog.Diagnostics;
    internal LocalizationFeatures.LanguageState State => new(DisplayCulture.Name, DisplayCulture.TextInfo.IsRightToLeft, _catalog.Languages);
    internal event EventHandler? Changed;

    public string Text(string slug, IReadOnlyDictionary<string, object>? arguments = null) =>
        _catalog.Text(new LocaleCatalog.TextSlug(slug), DisplayCulture, arguments, FormattingCulture);

    public LocaleCatalog.ImageResource Image(string slug, Func<LocaleCatalog.ImageResource, bool>? accepts = null) =>
        _catalog.Image(new LocaleCatalog.ImageSlug(slug), DisplayCulture, accepts);

    internal Bitmap? Decode(LocaleCatalog.ImageResource image)
    {
        if (_images.TryGetValue(image, out var existing))
            return existing;

        try
        {
            using var input = new MemoryStream(image.Bytes, writable: false);
            Bitmap bitmap;
            if (image.Kind == LocaleCatalog.ImageKind.Svg)
            {
                using var svg = new SKSvg();
                var picture = svg.Load(input);
                if (picture is null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
                    return null;
                var scale = 96f / Math.Max(picture.CullRect.Width, picture.CullRect.Height);
                using var output = new MemoryStream();
                if (!svg.Save(output, SKColors.Transparent, SKEncodedImageFormat.Png, 100, scale, scale))
                    return null;
                output.Position = 0;
                bitmap = new Bitmap(output);
            }
            else
            {
                bitmap = new Bitmap(input);
            }
            _images.Add(image, bitmap);
            return bitmap;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Xml.XmlException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
            image.Dispose();
        _images.Clear();
    }

    internal FeatureOutcome Select(string tag)
    {
        CultureInfo selected;
        try { selected = LanguagePack.ParseCulture(tag); }
        catch (FormatException) { return new("invalid_locale", "Use a valid BCP 47 language tag."); }
        if (string.Equals(selected.Name, DisplayCulture.Name, StringComparison.Ordinal))
            return FeatureOutcome.Success("Display language selected.", State);

        try { SavePreference(selected.Name); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new("language_save_failed", "Unable to save the display language preference.");
        }

        DisplayCulture = selected;
        Changed?.Invoke(this, EventArgs.Empty);
        return FeatureOutcome.Success("Display language selected.", State);
    }

    internal void Observe(Control control, Action update)
    {
        update();
        control.AttachedToVisualTree += (_, _) =>
        {
            Changed += Apply;
            update();
        };
        control.DetachedFromVisualTree += (_, _) => Changed -= Apply;

        void Apply(object? sender, EventArgs args) => update();
    }

    private CultureInfo? LoadPreference()
    {
        if (_settings is null)
            return null;
        try { return LanguagePack.ParseCulture(File.ReadAllText(_settings).Trim()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { return null; }
    }

    private void SavePreference(string locale)
    {
        if (_settings is null)
            return;
        var folder = Directory.CreateDirectory(Path.GetDirectoryName(_settings)!).FullName;
        var temporary = Path.Combine(folder, $".language-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, locale);
            File.Move(temporary, _settings, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
