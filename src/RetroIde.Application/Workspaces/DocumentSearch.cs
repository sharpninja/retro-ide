namespace RetroIde.Application.Workspaces;

internal static class DocumentSearch
{
    public static int Find(string text, string query, int position, bool matchCase, bool backward)
    {
        var comparison = Comparison(matchCase);
        if (!backward)
        {
            var match = text.IndexOf(query, position, comparison);
            return match >= 0 ? match : text.IndexOf(query, comparison);
        }

        if (position > 0)
        {
            var match = text.AsSpan(0, position).LastIndexOf(query.AsSpan(), comparison);
            if (match >= 0)
                return match;
        }
        return text.LastIndexOf(query, comparison);
    }

    public static IReadOnlyList<int> FindAll(string text, string query, bool matchCase)
    {
        var matches = new List<int>();
        var comparison = Comparison(matchCase);
        var start = 0;
        while (text.IndexOf(query, start, comparison) is var index && index >= 0)
        {
            matches.Add(index);
            start = index + query.Length;
        }
        return matches;
    }

    public static StringComparison Comparison(bool matchCase) => matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
