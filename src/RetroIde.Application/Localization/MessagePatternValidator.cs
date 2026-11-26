using System.Globalization;

namespace RetroIde.Application.Localization;

internal sealed class MessagePatternValidator(string pattern, IReadOnlyDictionary<string, string> types)
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private int _position;

    public void Validate()
    {
        ReadMessage(0);
        if (_used.Count != types.Count)
            throw Invalid();
    }

    private void ReadMessage(int depth)
    {
        if (depth > 24)
            throw Invalid();

        while (_position < pattern.Length)
        {
            switch (pattern[_position++])
            {
                case '\'': ReadQuote(); break;
                case '{': ReadArgument(depth); break;
                case '}':
                    if (depth == 0)
                        throw Invalid();
                    return;
            }
        }
        if (depth != 0)
            throw Invalid();
    }

    private void ReadQuote()
    {
        if (Read('\''))
            return;
        if (_position == pattern.Length || pattern[_position] is not ('{' or '}' or '#'))
            return;

        while (_position < pattern.Length)
        {
            if (pattern[_position++] == '\'' && !Read('\''))
                return;
        }
        throw Invalid();
    }

    private void ReadArgument(int depth)
    {
        var argument = ReadToken();
        if (!types.TryGetValue(argument, out var type))
            throw Invalid();
        _used.Add(argument);
        SkipSpaces();
        if (Read('}'))
            return;
        Require(',');
        var operation = ReadToken();
        SkipSpaces();
        switch (operation)
        {
            case "select" when type == "text":
                Require(',');
                ReadOptions(depth, numeric: false);
                break;
            case "plural" or "selectordinal" when type == "number":
                Require(',');
                ReadOptions(depth, numeric: true);
                break;
            case "number" when type == "number":
                ReadStyle(["integer", "currency", "percent"]);
                break;
            case "date" when type == "date":
                ReadStyle(["short", "full"]);
                break;
            case "time" when type == "date":
                ReadStyle(["short", "medium"]);
                break;
            default:
                throw Invalid();
        }
    }

    private void ReadStyle(string[] supported)
    {
        if (Read(',') && !supported.Contains(ReadToken(), StringComparer.Ordinal))
            throw Invalid();
        SkipSpaces();
        Require('}');
    }

    private void ReadOptions(int depth, bool numeric)
    {
        var options = new HashSet<string>(StringComparer.Ordinal);
        SkipSpaces();
        if (numeric && pattern.AsSpan(_position).StartsWith("offset:", StringComparison.Ordinal))
        {
            _position += "offset:".Length;
            if (!int.TryParse(ReadToken(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw Invalid();
        }

        SkipSpaces();
        while (!Read('}'))
        {
            var key = ReadToken();
            if (!options.Add(key) || (numeric && !IsPluralKey(key)))
                throw Invalid();
            SkipSpaces();
            Require('{');
            ReadMessage(depth + 1);
            SkipSpaces();
        }
        if (!options.Contains("other"))
            throw Invalid();
    }

    private static bool IsPluralKey(string key) =>
        key is "zero" or "one" or "two" or "few" or "many" or "other" ||
        (key.StartsWith('=') && decimal.TryParse(key.AsSpan(1), NumberStyles.Number, CultureInfo.InvariantCulture, out _));

    private string ReadToken()
    {
        SkipSpaces();
        var start = _position;
        while (_position < pattern.Length && !char.IsWhiteSpace(pattern[_position]) && pattern[_position] is not ('{' or '}' or ','))
            _position++;
        if (_position == start)
            throw Invalid();
        return pattern[start.._position];
    }

    private void SkipSpaces()
    {
        while (_position < pattern.Length && char.IsWhiteSpace(pattern[_position]))
            _position++;
    }

    private bool Read(char expected)
    {
        if (_position == pattern.Length || pattern[_position] != expected)
            return false;
        _position++;
        return true;
    }

    private void Require(char expected)
    {
        if (!Read(expected))
            throw Invalid();
    }

    private static FormatException Invalid() => new("An ICU message pattern or its declared argument types are invalid.");
}
