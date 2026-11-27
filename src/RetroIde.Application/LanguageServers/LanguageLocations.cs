using System.Text.Json;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageLocation(string Uri, LanguageRange Range)
{
    internal static LanguageLocation[] Read(JsonElement result)
    {
        return result.ValueKind switch
        {
            JsonValueKind.Null => [],
            JsonValueKind.Object => [Parse(result)],
            JsonValueKind.Array => result.EnumerateArray().Select(Parse).Distinct().ToArray(),
            _ => throw new InvalidDataException("The language server returned invalid source locations."),
        };
    }

    private static LanguageLocation Parse(JsonElement location)
    {
        if (location.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The language server location is not an object.");
        var link = location.TryGetProperty("targetUri", out var uri);
        if (!link && !location.TryGetProperty("uri", out uri) || uri.ValueKind != JsonValueKind.String ||
            uri.GetString() is not { Length: > 0 } path ||
            !location.TryGetProperty(link ? "targetSelectionRange" : "range", out var member) ||
            member.Deserialize<LanguageRange>(WireJson.Options) is not { IsOrdered: true } range)
            throw new InvalidDataException("The language server location has no valid URI or range.");
        return new(path, range);
    }
}

internal sealed record LanguageNavigationResult(DocumentContext Document, LanguageServerStatus Server, LanguageLocation[] Targets);
internal sealed record LanguageOpenedLocation(DocumentContext Document, int Offset, SourcePosition Position);

internal sealed record LanguageTargetFile(WorkspaceExplorer.SourceAddress Address, LanguageDocument? OpenBuffer,
    WorkspaceExplorer.SourceFile? Source)
{
    internal string Text => OpenBuffer?.Text ?? Source!.Text;

    internal static WorkspaceExplorer.SourceAddress? Resolve(WorkspaceExplorer explorer, string uri, Guid preferredRoot)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile ||
            parsed.Query.Length != 0 || parsed.Fragment.Length != 0 || !Path.IsPathFullyQualified(parsed.LocalPath))
            return null;
        try
        {
            var path = Path.GetFullPath(parsed.LocalPath);
            var roots = explorer.Roots.Where(root => NativeGit.ContainsPath(root.Path, path))
                .OrderByDescending(root => root.Id == preferredRoot).ThenByDescending(root => root.Path.Length);
            var root = roots.FirstOrDefault();
            return root is null ? null : explorer.ResolveSource(new(root.Id, Path.GetRelativePath(root.Path, path)));
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    internal static async Task<LanguageTargetFile?> ReadAsync(ILanguageRegistration registration,
        WorkspaceExplorer.SourceAddress address, CancellationToken token)
    {
        if (registration.LanguageServers.ReadObserved(address.Entry) is { } open)
            return new(address, open, null);

        var source = await WorkspaceExplorer.ReadSourceAsync(address, token).ConfigureAwait(false);
        return source is not null ? new(address, null, source) : null;
    }

    internal bool IsCurrent(ILanguageRegistration registration)
    {
        if (!registration.Explorer.Roots.Any(root => root.Id == Address.Entry.RootId &&
            string.Equals(root.Path, Address.RootPath, StringComparison.Ordinal)))
            return false;

        return OpenBuffer is { } open
            ? registration.ReadLanguageDocument(open.Context) is not null
            : !registration.HasOpenDocument(Address.Entry);
    }
}
