namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageProblemSnapshot(LanguageProblem[] Items, bool Truncated)
{
    internal const int MaximumItems = 1000;

    internal static LanguageProblemSnapshot Capture(IEnumerable<LanguageProblem> problems)
    {
        var items = problems.Take(MaximumItems + 1).ToArray();
        return items.Length > MaximumItems ? new(items[..MaximumItems], Truncated: true) : new(items, Truncated: false);
    }
}
