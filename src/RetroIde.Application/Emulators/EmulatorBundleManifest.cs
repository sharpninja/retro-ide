using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetroIde.Application.Emulators;

/// <summary>Reads a versioned bucket manifest without resolving or running its artifacts.</summary>
/// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest.</remarks>
internal sealed partial record EmulatorBundleManifest(
    string Id, string Version, string Name, string Description, Uri Homepage, string License, string AdapterId,
    IReadOnlyList<Version> AdapterApi, IReadOnlyDictionary<string, EmulatorBundleArtifact> Targets,
    IReadOnlyDictionary<string, EmulatorDebuggerProfileAsset> DebuggerProfiles)
{
    internal const int MaximumBytes = 1024 * 1024;

    internal static EmulatorBundleManifest Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaximumBytes)
            throw new InvalidDataException("The bundle manifest exceeds its permitted size.");

        try
        {
            using var document = JsonDocument.Parse(utf8);
            return ReadManifest(document.RootElement);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The bundle manifest is not valid JSON.", error);
        }
    }

    internal EmulatorBundleArtifact TargetFor(string runtimeIdentifier, Version adapterApi)
    {
        if (!Targets.TryGetValue(runtimeIdentifier, out var artifact))
            throw new InvalidOperationException("The bundle does not declare the requested host target.");
        if (!AdapterApi.Contains(adapterApi))
            throw new InvalidOperationException("The bundle does not support the requested adapter API version.");
        return artifact;
    }

    private static EmulatorBundleManifest ReadManifest(JsonElement root)
    {
        var manifest = AsObject(root, "manifest");
        if (ReadPositiveInt(manifest, "schemaVersion") != 1)
            throw Invalid("schemaVersion", "must be 1.");
        var version = ReadText(manifest, "version");
        if (!IsReleaseVersion(version))
            throw Invalid("version", "must be a SemVer release version.");
        var targets = ReadMap(Required(manifest, "targets"), "targets", ReadTarget);
        if (targets.Count == 0)
            throw Invalid("targets", "must declare at least one host runtime.");
        var profiles = manifest.TryGetProperty("debuggerProfiles", out var profileData)
            ? ReadMap(profileData, "debuggerProfiles", ReadProfile)
            : ReadOnlyDictionary<string, EmulatorDebuggerProfileAsset>.Empty;
        return new EmulatorBundleManifest(ReadId(manifest, "id"), version, ReadText(manifest, "name"), ReadText(manifest, "description"),
            ReadUri(manifest, "homepage"), ReadText(manifest, "license"), ReadId(manifest, "adapterId"),
            ReadAdapterVersions(manifest), targets, profiles);
    }

    private static EmulatorBundleArtifact ReadTarget(JsonElement value)
    {
        var target = AsObject(value, "targets");
        var digest = ReadText(target, "sha256");
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
            throw Invalid("sha256", "must be a 64-digit hexadecimal digest.");
        var paths = ReadMap(Required(target, "entryPoints"), "entryPoints", path => ReadArchivePath(path, "entryPoints"));
        if (!paths.ContainsKey("binding"))
            throw Invalid("entryPoints.binding", "is required.");
        return new EmulatorBundleArtifact(ReadUri(target, "url", allowFile: true), digest.ToLowerInvariant(), paths);
    }

    private static EmulatorDebuggerProfileAsset ReadProfile(JsonElement value)
    {
        var profile = AsObject(value, "debuggerProfiles");
        return new EmulatorDebuggerProfileAsset(ReadPositiveInt(profile, "schemaVersion"),
            ReadArchivePath(Required(profile, "path"), "debuggerProfiles.path"));
    }

    private static IReadOnlyList<Version> ReadAdapterVersions(JsonElement manifest)
    {
        var versions = Required(manifest, "adapterApi");
        if (versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() == 0)
            throw Invalid("adapterApi", "must contain at least one major.minor version.");
        var supported = new List<Version>();
        foreach (var value in versions.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !System.Version.TryParse(value.GetString(), out var version) ||
                version.Major < 1 || version.Build >= 0 || supported.Contains(version))
                throw Invalid("adapterApi", "must contain distinct positive major.minor versions.");
            supported.Add(version);
        }
        return supported.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, T> ReadMap<T>(JsonElement data, string field, Func<JsonElement, T> read)
    {
        var values = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in AsObject(data, field).EnumerateObject())
        {
            if (!IsIdentifier(item.Name))
                throw Invalid(field, "must use lowercase identifiers for its keys.");
            values.Add(item.Name, read(item.Value));
        }
        return new ReadOnlyDictionary<string, T>(values);
    }

    private static JsonElement AsObject(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid(field, "must be a JSON object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.EnumerateObject())
        {
            if (!names.Add(item.Name))
                throw Invalid(field + "." + item.Name, "is duplicated.");
        }
        return value;
    }

    private static JsonElement Required(JsonElement value, string field) =>
        value.TryGetProperty(field, out var item) ? item : throw Invalid(field, "is required.");

    private static string ReadText(JsonElement value, string field) => AsText(Required(value, field), field);

    private static string AsText(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text : throw Invalid(field, "must be a nonempty string.");

    private static int ReadPositiveInt(JsonElement value, string field) =>
        Required(value, field) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var result) && result > 0
            ? result : throw Invalid(field, "must be a positive integer.");

    private static string ReadId(JsonElement value, string field)
    {
        var id = ReadText(value, field);
        return IsIdentifier(id) ? id : throw Invalid(field, "must be a lowercase identifier.");
    }

    internal static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 96 && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(static item => char.IsAsciiLetterLower(item) || char.IsAsciiDigit(item) || item is '-' or '_') &&
        BundleArchivePath.IsPortableSegment(value);

    internal static bool IsReleaseVersion(string value) => value.Length <= 128 && ReleaseVersion().IsMatch(value);

    private static Uri ReadUri(JsonElement value, string field, bool allowFile = false)
    {
        if (!Uri.TryCreate(ReadText(value, field), UriKind.Absolute, out var uri) ||
            !(uri.Scheme is "https" or "http" || (allowFile && uri.IsFile)) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Invalid(field, "must be an absolute artifact or web URL without credentials or a fragment.");
        return uri;
    }

    private static string ReadArchivePath(JsonElement value, string field)
    {
        var path = AsText(value, field);
        return BundleArchivePath.IsPortable(path) ? path : throw Invalid(field, "must be a portable archive-relative path using forward slashes.");
    }

    private static InvalidDataException Invalid(string field, string message) => new($"Bundle manifest field '{field}' {message}");

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ReleaseVersion();
}

internal sealed record EmulatorBundleArtifact(Uri Url, string Sha256, IReadOnlyDictionary<string, string> EntryPoints);

internal sealed record EmulatorDebuggerProfileAsset(int SchemaVersion, string Path);
