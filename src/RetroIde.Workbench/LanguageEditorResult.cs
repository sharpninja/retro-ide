using System.Globalization;
using System.Text;
using System.Text.Json;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed record LanguageEditorResult(string? Message, IReadOnlyList<LanguageEditorRow> Rows, Guid? EditPreviewId = null)
{
    internal static LanguageEditorResult Hover(JsonElement data)
    {
        var query = data.Deserialize<LanguageQueryResult>(WireJson.Options)!;
        return new(PlainText(Member(query.Result, "contents")), []);
    }

    internal static LanguageEditorResult Signatures(JsonElement data)
    {
        var query = data.Deserialize<LanguageQueryResult>(WireJson.Options)!;
        return new(null, Items(Member(query.Result, "signatures"))
            .Select(item => new LanguageEditorRow(Text(item, "label") ?? "", PlainText(Member(item, "documentation"))))
            .Where(row => row.Label.Length != 0).ToArray());
    }

    internal static LanguageEditorResult Completions(JsonElement data)
    {
        var query = data.Deserialize<LanguageQueryResult>(WireJson.Options)!;
        return new(null, LanguageCompletionOffer.ReadItems(query.Result).Select((item, index) =>
            new LanguageEditorRow(Text(item, "label") ?? "", Text(item, "detail") ?? PlainText(Member(item, "documentation")),
                query.CompletionOfferId is { } offer ? new LanguageServerFeatures.PreviewCompletionInput(query.Document, offer, index) : null))
            .Where(row => row.Label.Length != 0).ToArray());
    }

    internal static LanguageEditorResult Navigation(JsonElement data)
    {
        var result = data.Deserialize<LanguageNavigationResult>(WireJson.Options)!;
        return new(null, result.Targets.Select(target => LocationRow(result.Document, result.Server.SessionId, target)).ToArray());
    }

    internal static LanguageEditorResult Symbols(JsonElement data, string uri)
    {
        var query = data.Deserialize<LanguageQueryResult>(WireJson.Options)!;
        var rows = new List<LanguageEditorRow>();
        AddSymbols(query.Result, uri, query, rows, depth: 0);
        return new(null, rows);
    }

    private static void AddSymbols(JsonElement items, string uri, LanguageQueryResult query, List<LanguageEditorRow> rows, int depth)
    {
        foreach (var symbol in Items(items))
        {
            if (rows.Count >= MaximumRows)
                return;
            if (Text(symbol, "name") is { Length: > 0 } name)
            {
                var location = Member(symbol, "location");
                var range = location.ValueKind == JsonValueKind.Object ? Member(location, "range") : Member(symbol, "selectionRange");
                var request = Location(query.Document, query.Server.SessionId, Text(location, "uri") ?? uri, range);
                rows.Add(new(new string(' ', depth * 2) + name, Text(symbol, "detail"), request));
            }
            AddSymbols(Member(symbol, "children"), uri, query, rows, depth + 1);
        }
    }

    internal static LanguageEditorResult Diagnostics(JsonElement data, string uri)
    {
        var result = data.Deserialize<LanguageDiagnosticResult>(WireJson.Options)!;
        return new(null, result.Items.Take(MaximumRows).Select(item =>
        {
            var request = Location(result.Document, result.SessionId, uri, Member(item, "range"));
            return new LanguageEditorRow(Text(item, "message") ?? "", request is null ? null : Position(request.Target.Range),
                request);
        }).Where(row => row.Label.Length != 0).ToArray());
    }

    internal static LanguageEditorResult Actions(JsonElement data)
    {
        var offer = data.Deserialize<LanguageCodeActionList>(WireJson.Options)!;
        return new(null, offer.Actions.Select((item, index) => new LanguageEditorRow(Text(item, "title") ?? "",
            Text(Member(item, "disabled"), "reason") ?? Text(item, "kind"),
            Member(item, "disabled").ValueKind == JsonValueKind.Undefined &&
            Member(item, "command").ValueKind == JsonValueKind.Undefined
                ? new LanguageServerFeatures.PreviewActionInput(offer.Document, offer.OfferId, index) : null))
            .Where(row => row.Label.Length != 0).ToArray());
    }

    internal static LanguageEditorResult Preview(JsonElement data)
    {
        var preview = data.Deserialize<LanguageEditPreview>(WireJson.Options)!;
        if (preview.Targets.Length == 0)
            return new(null, []);

        var message = new StringBuilder();
        foreach (var target in preview.Targets)
        {
            message.AppendLine(target.RelativePath);
            foreach (var edit in target.Edits)
            {
                message.Append(CultureInfo.InvariantCulture, $"@@ {edit.Offset} +{edit.Length} @@").AppendLine();
                AppendEditLines(message, '-', edit.PreviousText ?? "");
                AppendEditLines(message, '+', edit.NewText);
            }
        }
        return new(message.ToString(), [], preview.PreviewId);
    }

    private static void AppendEditLines(StringBuilder message, char prefix, string text)
    {
        if (text.Length == 0)
            return;

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            message.Append(prefix).Append(' ').AppendLine(line);
    }

    private static LanguageEditorRow LocationRow(DocumentContext document, Guid? session, LanguageLocation target)
    {
        var path = Uri.TryCreate(target.Uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : target.Uri;
        return new(path, Position(target.Range), session is { } id ? new LanguageServerFeatures.OpenLocationInput(document, id, target) : null);
    }

    private static LanguageServerFeatures.OpenLocationInput? Location(DocumentContext document, Guid? session, string uri, JsonElement range)
    {
        if (session is not { } id || range.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            return range.Deserialize<LanguageRange>(WireJson.Options) is { IsOrdered: true } parsed
                ? new(document, id, new(uri, parsed)) : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Position(LanguageRange range) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)range.Start.Line + 1}:{(long)range.Start.Character + 1}");

    private const int MaximumRows = 256;

    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Take(MaximumRows) : [];

    private static string? PlainText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object => Text(element, "value"),
        JsonValueKind.Array => string.Join(Environment.NewLine, Items(element).Select(PlainText)),
        _ => null,
    };

    private static string? Text(JsonElement element, string key)
    {
        var value = Member(element, key);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonElement Member(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : default;
}
