using System.Text;
using System.Text.RegularExpressions;

namespace RetroIde.Application.LanguageServers;

internal static class LspGlob
{
    internal static Regex Compile(string pattern)
    {
        if (pattern.Length is 0 or > 512 || pattern.Contains('\0'))
            throw new FormatException("Invalid document filter pattern.");

        var expression = new StringBuilder("\\A");
        var groups = 0;
        for (var index = 0; index < pattern.Length; index++)
        {
            switch (pattern[index])
            {
                case '*':
                    AppendWildcard(pattern, expression, ref index);
                    break;
                case '?':
                    expression.Append("[^/]");
                    break;
                case '{' when groups < 8:
                    expression.Append("(?:");
                    groups++;
                    break;
                case '}' when groups > 0:
                    expression.Append(')');
                    groups--;
                    break;
                case '}' or '{' or ']':
                    throw new FormatException("Unmatched document filter delimiter.");
                case ',' when groups > 0:
                    expression.Append('|');
                    break;
                case '[':
                    AppendClass(pattern, expression, ref index);
                    break;
                default:
                    expression.Append(Regex.Escape(pattern[index].ToString()));
                    break;
            }
        }
        if (groups != 0)
            throw new FormatException("Unclosed document filter group.");

        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
            options |= RegexOptions.IgnoreCase;
        return new Regex(expression.Append("\\z").ToString(), options, TimeSpan.FromMilliseconds(100));
    }

    private static void AppendWildcard(string pattern, StringBuilder expression, ref int index)
    {
        if (index + 1 == pattern.Length || pattern[index + 1] != '*')
        {
            expression.Append("[^/]*");
            return;
        }

        index++;
        if (index + 1 < pattern.Length && pattern[index + 1] == '/')
        {
            expression.Append("(?:[^/]+/)*");
            index++;
        }
        else
            expression.Append("[\\s\\S]*");
    }

    private static void AppendClass(string pattern, StringBuilder expression, ref int index)
    {
        var end = pattern.IndexOf(']', index + 1);
        var start = index + 1;
        var negative = start < pattern.Length && pattern[start] == '!';
        if (negative)
            start++;
        if (end <= start || pattern.AsSpan(start, end - start).IndexOfAny('/', '[') >= 0)
            throw new FormatException("Invalid document filter character class.");

        expression.Append(negative ? "[^/" : "[");
        for (var i = start; i < end; i++)
        {
            var value = pattern[i];
            if (value is '\\' or '^')
                expression.Append('\\');
            expression.Append(value);
        }
        expression.Append(negative ? "]" : "-[/]]");
        index = end;
    }
}
