using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RetroIde.Application.Localization;

internal static partial class PackImageLoader
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int MaxImageDimension = 4096;

    public static LocaleCatalog.ImageResource Load(string directory, JsonElement entry)
    {
        var asset = entry.GetString()?.Replace('\\', '/') ?? throw new FormatException("An image needs a relative path.");
        if (Path.IsPathRooted(asset) || asset.Contains(':', StringComparison.Ordinal))
            throw new FormatException("An image must be stored inside its language pack.");

        var path = directory;
        foreach (var part in asset.Split('/'))
        {
            if (part is "" or "." or "..")
                throw new FormatException("An image path cannot leave its language pack.");
            path = Path.Combine(path, part);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new FormatException("Image paths cannot traverse symbolic links.");
        }
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > MaxImageBytes)
            throw new FormatException("The image exceeds the language-pack image size limit.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => ReadSvg(bytes),
            ".png" => ReadPng(bytes),
            _ => throw new FormatException("Language pack images must be SVG or PNG."),
        };
    }

    private static LocaleCatalog.ImageResource ReadPng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) is <= 0 or > MaxImageDimension ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) is <= 0 or > MaxImageDimension)
            throw new FormatException("Invalid PNG geometry.");
        return new(LocaleCatalog.ImageKind.Png, bytes);
    }

    private static LocaleCatalog.ImageResource ReadSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg"))
            throw new FormatException("Invalid SVG root element.");

        foreach (var item in root.DescendantsAndSelf())
        {
            if (item.Name.LocalName is "script" or "foreignObject")
                throw new FormatException("Language pack SVG files cannot load active content.");
            if (item.Name.LocalName == "style")
                CheckLinks(item.Value);
            foreach (var attribute in item.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                var name = attribute.Name.LocalName;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attribute.Name == XNamespace.Xml + "base" ||
                    (name is "href" or "src" && !attribute.Value.StartsWith('#')))
                    throw new FormatException("Language pack SVG files cannot access external resources.");
                CheckLinks(attribute.Value);
            }
        }
        return new(LocaleCatalog.ImageKind.Svg, bytes);
    }

    private static void CheckLinks(string source)
    {
        if (source.Contains("@import", StringComparison.OrdinalIgnoreCase) ||
            Link().Matches(source).Any(match => !match.Groups[1].Value.Trim().Trim('\'', '"').StartsWith('#')))
            throw new FormatException("SVG styles can only refer to resources in the same SVG.");
    }

    [GeneratedRegex(@"url\s*\(([^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Link();
}
