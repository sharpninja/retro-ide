using System.Buffers;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageCompletionOffer(LanguageQueryResult Query, int Offset, JsonElement[] Items)
{
    internal static JsonElement[] ReadItems(JsonElement result)
    {
        var items = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out var list)
            ? list : result;
        return items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Take(256).ToArray() : [];
    }

    internal JsonElement Prepare(int index, string document)
    {
        var item = Items[index];
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("label", out var label) ||
            label.ValueKind != JsonValueKind.String || !IsPlainText(item))
            throw new InvalidDataException("The completion does not contain a supported plain-text insertion.");

        var encoding = Query.Server.PositionEncoding!;
        var position = LspPosition.AtOffset(document, Offset, encoding);
        var primary = item.TryGetProperty("textEdit", out var edit)
            ? ReadPrimaryEdit(edit, position)
            : DefaultEdit(document, encoding, position, item, label);
        var additional = ReadAdditionalEdits(item);
        ValidateDistinctRanges(primary.Range, additional);
        return JsonSerializer.SerializeToElement(additional.Prepend(primary), WireJson.Options);
    }

    private static bool IsPlainText(JsonElement item) => !item.TryGetProperty("insertTextFormat", out var format) ||
        format.ValueKind == JsonValueKind.Number && format.TryGetInt32(out var value) && value == 1;

    private static Edit ReadPrimaryEdit(JsonElement edit, LspPosition caret)
    {
        if (edit.ValueKind != JsonValueKind.Object || !edit.TryGetProperty("newText", out var replacement) ||
            replacement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The completion has no replacement text.");

        var range = edit.TryGetProperty("range", out var explicitRange) ? ParseRange(explicitRange) : ReadInsertRange(edit);
        if (range.Start.Line != caret.Line || range.End.Line != caret.Line ||
            caret.Character < range.Start.Character || caret.Character > range.End.Character)
            throw new InvalidDataException("The completion's edit does not cover its requested caret position.");
        return new(range, replacement.GetString()!);
    }

    private static LanguageRange ReadInsertRange(JsonElement edit)
    {
        if (!edit.TryGetProperty("insert", out var insert) || !edit.TryGetProperty("replace", out var replace))
            throw new InvalidDataException("The completion has no edit range.");
        var prefix = ParseRange(insert);
        var whole = ParseRange(replace);
        if (prefix.Start != whole.Start || whole.End.Line != prefix.End.Line || whole.End.Character < prefix.End.Character)
            throw new InvalidDataException("The completion insert range is not a prefix of its replace range.");
        return prefix;
    }

    private Edit DefaultEdit(string document, string encoding, LspPosition position, JsonElement item, JsonElement label)
    {
        var start = Offset;
        while (start > 0 && Rune.DecodeLastFromUtf16(document.AsSpan(0, start), out var rune, out var width) == OperationStatus.Done &&
            (Rune.IsLetterOrDigit(rune) || rune.Value == '_'))
            start -= width;

        var replacement = item.TryGetProperty("insertText", out var insert) && insert.ValueKind == JsonValueKind.String ? insert : label;
        return new(new(LspPosition.AtOffset(document, start, encoding), position), replacement.GetString()!);
    }

    private static Edit[] ReadAdditionalEdits(JsonElement item)
    {
        if (!item.TryGetProperty("additionalTextEdits", out var edits))
            return [];
        if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() >= 10000)
            throw new InvalidDataException("The completion's additional edits are invalid or too large.");

        return edits.EnumerateArray().Select(edit =>
        {
            if (edit.ValueKind != JsonValueKind.Object || !edit.TryGetProperty("range", out var range) ||
                !edit.TryGetProperty("newText", out var text) || text.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("An additional completion edit is malformed.");
            return new Edit(ParseRange(range), text.GetString()!);
        }).ToArray();
    }

    private static LanguageRange ParseRange(JsonElement range) =>
        range.ValueKind == JsonValueKind.Object && range.Deserialize<LanguageRange>(WireJson.Options) is { IsOrdered: true } positions
            ? positions : throw new InvalidDataException("The completion contains an invalid source range.");

    private static void ValidateDistinctRanges(LanguageRange primary, Edit[] additional)
    {
        var ranges = additional.Select(edit => edit.Range).Prepend(primary)
            .OrderBy(range => range.Start.Line).ThenBy(range => range.Start.Character).ToArray();
        for (var index = 1; index < ranges.Length; index++)
            if (ranges[index - 1].Start == ranges[index].Start)
                throw new InvalidDataException("Completion edits cannot start at the same position.");
    }

    private sealed record Edit(LanguageRange Range, string NewText);
}
