using System.Text.Json;
using System.Text.RegularExpressions;
using RetroIde.Application.Git;

namespace RetroIde.Application.LanguageServers;

internal sealed class LspDocumentSelector
{
    private readonly Filter[]? _filters;

    private LspDocumentSelector(Filter[]? filters) => _filters = filters;

    internal static LspDocumentSelector Read(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object ||
            !options.TryGetProperty("documentSelector", out var selector) || selector.ValueKind == JsonValueKind.Null)
            return new(null);
        if (selector.ValueKind != JsonValueKind.Array || selector.GetArrayLength() > 64)
            throw new FormatException("Invalid document selector.");

        return new(selector.EnumerateArray().Select(Filter.Read).ToArray());
    }

    internal bool Matches(string language, string path, string root) =>
        _filters is null || _filters.Any(filter => filter.Matches(language, path, root));

    private sealed record Filter(string? Language, string? Scheme, Regex? Glob, string? Base, bool FilenameOnly, bool Absolute)
    {
        internal static Filter Read(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new FormatException("Invalid document filter.");
            if (item.TryGetProperty("notebook", out _))
                return new(null, "notebook", null, null, false, false);

            var language = OptionalString(item, "language");
            var scheme = OptionalString(item, "scheme");
            if (!item.TryGetProperty("pattern", out var pattern))
                return language is not null || scheme is not null ? new(language, scheme, null, null, false, false)
                    : throw new FormatException("A filter must specify a language, a scheme or a pattern.");

            if (pattern.ValueKind == JsonValueKind.String && pattern.GetString() is { } text)
                return new(language, scheme, LspGlob.Compile(text), null, !text.Contains('/'), Path.IsPathFullyQualified(text));
            if (pattern.ValueKind != JsonValueKind.Object || !pattern.TryGetProperty("baseUri", out var location) ||
                OptionalString(pattern, "pattern") is not { } relative)
                throw new FormatException("Invalid relative document pattern.");
            var uri = location.ValueKind == JsonValueKind.Object ? OptionalString(location, "uri")
                : location.ValueKind == JsonValueKind.String ? location.GetString() : null;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                throw new FormatException("Invalid document pattern base URI.");
            if (!parsed.IsFile)
                return new(language, "non-file", null, null, false, false);
            if (parsed.Query.Length != 0 || parsed.Fragment.Length != 0 || !Path.IsPathFullyQualified(parsed.LocalPath) ||
                Path.IsPathFullyQualified(relative))
                throw new FormatException("Invalid relative document pattern base.");

            return new(language, scheme, LspGlob.Compile(relative), parsed.LocalPath, false, false);
        }

        private static string? OptionalString(JsonElement item, string field)
        {
            if (!item.TryGetProperty(field, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
                ? text : throw new FormatException("The document filter field must be a nonempty string.");
        }

        internal bool Matches(string language, string path, string root)
        {
            if ((Language is not (null or "*") && !string.Equals(Language, language, StringComparison.Ordinal)) ||
                (Scheme is not null && !string.Equals(Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (Glob is null)
                return true;
            if (Base is { } location && !NativeGit.ContainsPath(location, path))
                return false;

            var source = Absolute ? path : FilenameOnly ? Path.GetFileName(path) : Path.GetRelativePath(Base ?? root, path);
            if (Path.DirectorySeparatorChar != '/')
                source = source.Replace(Path.DirectorySeparatorChar, '/');
            try { return Glob.IsMatch(source); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
