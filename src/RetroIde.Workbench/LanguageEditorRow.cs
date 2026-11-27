namespace RetroIde.Workbench;

internal sealed record LanguageEditorRow(string Label, string? Detail = null, object? Request = null)
{
    public override string ToString() => Label;
}
