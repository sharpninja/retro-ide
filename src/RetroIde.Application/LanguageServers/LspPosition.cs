using System.Text;

namespace RetroIde.Application.LanguageServers;

internal readonly record struct LspPosition(int Line, int Character)
{
    private static readonly char[] Newlines = ['\r', '\n'];

    internal static LspPosition AtOffset(string text, int offset, string encoding)
    {
        if (offset < 0 || offset > text.Length || !IsBoundary(text, offset))
            throw new ArgumentOutOfRangeException(nameof(offset), "Use an offset between source characters or line endings.");

        var line = 0;
        var start = 0;
        while (start < offset)
        {
            var newline = text.AsSpan(start, offset - start).IndexOfAny(Newlines);
            if (newline < 0)
                break;
            start += newline + 1;
            if (text[start - 1] == '\r' && start < offset && text[start] == '\n')
                start++;
            line++;
        }
        var part = text.AsSpan(start, offset - start);
        var units = part.Length;
        if (encoding == "utf-8")
            units = Encoding.UTF8.GetByteCount(part);
        else if (encoding == "utf-32")
        {
            units = 0;
            foreach (var rune in part.EnumerateRunes())
                units++;
        }
        return new LspPosition(line, units);
    }

    internal static object IncrementalChange(string before, string after, string encoding)
    {
        var prefix = CommonPrefix(before, after);
        var suffix = CommonSuffix(before, after, prefix);
        return new
        {
            range = new { start = AtOffset(before, prefix, encoding), end = AtOffset(before, before.Length - suffix, encoding) },
            text = after.Substring(prefix, after.Length - prefix - suffix),
        };
    }

    private static int CommonPrefix(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < limit && left[index] == right[index])
            index++;
        while (!IsBoundary(left, index) || !IsBoundary(right, index))
            index--;
        return index;
    }

    private static int CommonSuffix(string left, string right, int prefix)
    {
        var limit = Math.Min(left.Length, right.Length) - prefix;
        var count = 0;
        while (count < limit && left[^(count + 1)] == right[^(count + 1)])
            count++;
        while (!IsBoundary(left, left.Length - count) || !IsBoundary(right, right.Length - count))
            count--;
        return count;
    }

    private static bool IsBoundary(string text, int offset) =>
        offset == 0 || offset == text.Length ||
        !(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]) ||
          text[offset - 1] == '\r' && text[offset] == '\n');
}
