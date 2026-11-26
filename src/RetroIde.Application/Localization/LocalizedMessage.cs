using System.Globalization;
using System.Text.Json;
using Jeffijoe.MessageFormat;

namespace RetroIde.Application.Localization;

internal sealed class LocalizedMessage(string value, IReadOnlyDictionary<string, string>? types = null)
{
    private IReadOnlyDictionary<string, string>? ArgumentTypes => types;

    internal bool HasSameArguments(LocalizedMessage source)
    {
        var sourceTypes = source.ArgumentTypes;
        return types is null || sourceTypes is null
            ? types is null && sourceTypes is null
            : types.Count == sourceTypes.Count && types.All(argument =>
                sourceTypes.TryGetValue(argument.Key, out var expected) && string.Equals(expected, argument.Value, StringComparison.Ordinal));
    }

    public static LocalizedMessage Load(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String)
            return new(entry.GetString()!);
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("pattern", out var pattern) ||
            pattern.ValueKind != JsonValueKind.String || !entry.TryGetProperty("arguments", out var arguments) ||
            arguments.ValueKind != JsonValueKind.Object)
            throw new FormatException("A message requires a pattern and named argument types.");

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in arguments.EnumerateObject())
        {
            if (argument.Value.ValueKind != JsonValueKind.String || argument.Value.GetString() is not ("text" or "number" or "date") ||
                !types.TryAdd(argument.Name, argument.Value.GetString()!))
                throw new FormatException("Invalid or duplicate message argument type.");
        }

        var input = pattern.GetString()!;
        new MessagePatternValidator(input, types).Validate();
        return new(input, types);
    }

    public string Format(CultureInfo messageCulture, CultureInfo formattingCulture, IReadOnlyDictionary<string, object>? arguments)
    {
        if (types is null)
        {
            if (arguments is { Count: > 0 })
                throw new FormatException("The resource does not take arguments.");
            return value;
        }

        if (arguments is null || arguments.Count != types.Count ||
            types.Any(parameter => !arguments.TryGetValue(parameter.Key, out var supplied) || !MatchesType(parameter.Value, supplied)))
            throw new FormatException("The supplied arguments do not match this message.");

        var formatter = new MessageFormatter(customValueFormatter: new CustomValueFormatters
        {
            Number = (CultureInfo _, object? input, string? style, out string? output) =>
            {
                var specifier = style switch { "integer" => "N0", "currency" => "C", "percent" => "P", _ => "G" };
                output = ((IFormattable)input!).ToString(specifier, formattingCulture);
                return true;
            },
            Date = (CultureInfo _, object? input, string? style, out string? output) =>
            {
                output = ((IFormattable)input!).ToString(style == "full" ? "D" : "d", formattingCulture);
                return true;
            },
            Time = (CultureInfo _, object? input, string? style, out string? output) =>
            {
                output = ((IFormattable)input!).ToString(style == "medium" ? "T" : "t", formattingCulture);
                return true;
            },
        });

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = formattingCulture;
            return formatter.FormatMessage(value, arguments, messageCulture);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FormatException("The message could not be formatted.", error);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static bool MatchesType(string type, object value) => type switch
    {
        "text" => value is string,
        "number" => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "date" => value is DateTime or DateTimeOffset,
        _ => false,
    };
}
