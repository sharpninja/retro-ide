using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageTextEdit(int Offset, int Length, string NewText);
internal sealed record LanguageEditTarget(Guid RootId, string RelativePath, LanguageTextEdit[] Edits);
internal sealed record LanguageEditPreview(Guid PreviewId, DocumentContext Document, LanguageServerStatus Server, LanguageEditTarget[] Targets);
internal sealed record LanguageCodeActionList(Guid OfferId, DocumentContext Document, LanguageServerStatus Server, JsonElement[] Actions)
{
    internal LanguageDocument[]? CapturedBuffers { get; init; }
}

internal sealed record LanguageEditPlan(Guid Id, DocumentContext Origin, LanguageServerStatus Server, LanguageBufferEdit[] Documents)
{
    internal LanguageEditPreview Describe() => new(Id, Origin, Server, Documents.Select(item =>
        new LanguageEditTarget(item.File.Address.Entry.RootId, item.File.Address.Entry.RelativePath, item.Edits)).ToArray());
}

internal sealed record LanguageBufferEdit(LanguageTargetFile File, LanguageTextEdit[] Edits);

internal sealed class LanguageReviewStore<T> where T : class
{
    private const int MaximumReviews = 16;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, (DateTimeOffset Created, T Item)> _items = [];

    internal void Add(Guid id, T item)
    {
        lock (_gate)
        {
            RemoveExpired(DateTimeOffset.UtcNow);
            if (_items.Count == MaximumReviews)
                _items.Remove(_items.MinBy(entry => entry.Value.Created).Key);
            _items.Add(id, (DateTimeOffset.UtcNow, item));
        }
    }

    internal T? Find(Guid id)
    {
        lock (_gate)
        {
            RemoveExpired(DateTimeOffset.UtcNow);
            return _items.TryGetValue(id, out var item) ? item.Item : null;
        }
    }

    internal void Remove(Guid id)
    {
        lock (_gate)
            _items.Remove(id);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var id in _items.Where(entry => now - entry.Value.Created > Lifetime).Select(entry => entry.Key).ToArray())
            _items.Remove(id);
    }
}

internal static class LanguageEditPlanner
{
    private const int MaxDocuments = 32;
    private const int MaxEdits = 10000;
    private const int MaxCharacters = 4 * 1024 * 1024;

    internal static async Task<LanguageEditPlan> PrepareAsync(LanguageQueryResult result, ILanguageRegistration registration,
        CancellationToken token, string? formattingUri = null)
    {
        if (result.Server is not { SessionId: { } serverId, PositionEncoding: { } encoding })
            throw new InvalidDataException("The edit has no active server or position encoding.");
        var proposed = formattingUri is null ? ReadWorkspaceEdit(result.Result) : [new Proposed(formattingUri, null, result.Result)];
        var changes = new List<LanguageBufferEdit>();
        var edited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var characters = 0L;
        var count = 0L;
        foreach (var item in proposed)
        {
            token.ThrowIfCancellationRequested();
            if (LanguageTargetFile.Resolve(registration.Explorer, item.Uri, result.Document.RootId) is not { } address ||
                await LanguageTargetFile.ReadAsync(registration, address, token).ConfigureAwait(false) is not { } file)
                throw new InvalidDataException("An edit target is outside the workspace or cannot be read.");
            if (!edited.Add(Path.GetFullPath(address.Entry.RelativePath, address.RootPath)))
                throw new InvalidDataException("A workspace edit cannot address the same file twice.");
            if (result.CapturedBuffers is { } buffers && buffers.FirstOrDefault(captured =>
                WorkspaceExplorer.AddressEquality.Equals(new(captured.Context.RootId, captured.Context.RelativePath), address.Entry))?.Context
                != file.OpenBuffer?.Context)
                throw new LanguageDocumentChangedException();
            if (item.Version is { } version && (file.OpenBuffer is not { } buffer ||
                registration.LanguageServers.ReadLspVersion(buffer, serverId) != version))
                throw new LanguageDocumentChangedException();

            var ranges = ParseEdits(item.Edits, file.Text, encoding);
            characters += file.Text.Length + ranges.Sum(edit => (long)edit.NewText.Length);
            count += ranges.Length;
            if (characters > MaxCharacters || count > MaxEdits)
                throw new InvalidDataException("The workspace edit exceeds the review limit.");
            if (ranges.Length != 0)
                changes.Add(new LanguageBufferEdit(file, ranges));
        }
        return new(Guid.NewGuid(), result.Document, result.Server, changes.ToArray());
    }

    private static Proposed[] ReadWorkspaceEdit(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null)
            return [];
        if (result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The workspace edit is not an object.");
        var documents = result.TryGetProperty("documentChanges", out var versioned);
        var changes = result.TryGetProperty("changes", out var unversioned);
        if (documents && changes)
            throw new InvalidDataException("The workspace edit supplies two competing change sets.");
        if (documents && versioned.ValueKind == JsonValueKind.Array && versioned.GetArrayLength() <= MaxDocuments)
            return versioned.EnumerateArray().Select(ParseDocument).ToArray();
        if (changes && unversioned.ValueKind == JsonValueKind.Object)
        {
            var edit = unversioned.EnumerateObject().Take(MaxDocuments + 1).ToArray();
            if (edit.Length <= MaxDocuments)
                return edit.Select(item => new Proposed(item.Name, null, item.Value)).ToArray();
        }
        if (!documents && !changes)
            return [];
        throw new InvalidDataException("The workspace edit has an invalid or oversized document list.");
    }

    private static Proposed ParseDocument(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("textDocument", out var document) ||
            document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("uri", out var uri) ||
            uri.ValueKind != JsonValueKind.String || !item.TryGetProperty("edits", out var changes))
            throw new InvalidDataException("Only source text document edits can be applied.");
        int? version = null;
        if (document.TryGetProperty("version", out var supplied) && supplied.ValueKind != JsonValueKind.Null)
        {
            if (supplied.ValueKind != JsonValueKind.Number || !supplied.TryGetInt32(out var value) || value < 0)
                throw new InvalidDataException("The workspace edit has an invalid document version.");
            version = value;
        }
        return new(uri.GetString()!, version, changes);
    }

    private static LanguageTextEdit[] ParseEdits(JsonElement edits, string text, string encoding)
    {
        if (edits.ValueKind == JsonValueKind.Null)
            return [];
        if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() > MaxEdits)
            throw new InvalidDataException("The source edit list is invalid or too large.");
        var ranges = edits.EnumerateArray().Select(item => ParseEdit(item, text, encoding))
            .OrderBy(edit => edit.Offset).ThenBy(edit => edit.Length).ToArray();
        var previousEnd = 0;
        foreach (var edit in ranges)
        {
            if (edit.Offset < previousEnd)
                throw new InvalidDataException("Source edit ranges overlap.");
            previousEnd = edit.Offset + edit.Length;
        }
        return ranges;
    }

    private static LanguageTextEdit ParseEdit(JsonElement item, string text, string encoding)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("range", out var range) ||
            range.Deserialize<LanguageRange>(WireJson.Options) is not { IsOrdered: true } coordinates ||
            !item.TryGetProperty("newText", out var replacement) || replacement.ValueKind != JsonValueKind.String ||
            replacement.GetString() is not { } value || value.Contains('\0'))
            throw new InvalidDataException("The language server supplied an invalid source edit.");
        var start = coordinates.Start.ToOffset(text, encoding);
        return new(start, coordinates.End.ToOffset(text, encoding) - start, value);
    }

    private sealed record Proposed(string Uri, int? Version, JsonElement Edits);
}
