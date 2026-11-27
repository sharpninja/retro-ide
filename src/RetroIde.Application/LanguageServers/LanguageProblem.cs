using System.Text.Json;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageProblem(DocumentContext Document, Guid SessionId, LanguageLocation Location, string Message,
    int? Severity, string? Source, string? Code)
{
    internal static IEnumerable<LanguageProblem> Read(LanguageDocument document, LanguageDiagnosticResult diagnostics)
    {
        if (diagnostics.SessionId is not { } session || diagnostics.Items.Length == 0)
            yield break;

        var uri = LanguageServerSession.FileUri(document.Path);
        foreach (var item in diagnostics.Items)
            if (Parse(item, uri, document.Context, session) is { } problem)
                yield return problem;
    }

    private static LanguageProblem? Parse(JsonElement item, string uri, DocumentContext document, Guid session)
    {
        if (item.ValueKind != JsonValueKind.Object || Text(item, "message") is not { Length: > 0 } message ||
            !item.TryGetProperty("range", out var range) || range.ValueKind != JsonValueKind.Object)
            return null;

        LanguageRange positions;
        try { positions = range.Deserialize<LanguageRange>(WireJson.Options); }
        catch (JsonException) { return null; }
        if (!positions.IsOrdered)
            return null;
        int? severity = item.TryGetProperty("severity", out var level) && level.ValueKind == JsonValueKind.Number &&
            level.TryGetInt32(out var number) ? number : null;
        var code = item.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText() : Text(item, "code");
        return new(document, session, new(uri, positions), message, severity, Text(item, "source"), code);
    }

    private static string? Text(JsonElement item, string member) =>
        item.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
