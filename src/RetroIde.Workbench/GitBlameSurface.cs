using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal static class GitBlameSurface
{
    internal static Control Create(GitBlameViewModel view)
    {
        var localization = view.Localization;
        var revision = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var source = view.Blame.Source;
        localization.Observe(revision, () => revision.Text = source.CommitId ?? localization.Text(WorkbenchText.GitBlameWorking));
        var path = new SelectableTextBlock { Text = Path.Combine(source.WorktreePath, source.Path), TextWrapping = TextWrapping.Wrap };
        var refresh = CommandPresentation.CreateButton(WorkbenchCommands.RefreshGitBlame, view.RefreshCommand, localization);
        var status = new TextBlock();
        status.Bind(TextBlock.TextProperty, new Binding(nameof(view.LastResult) + ".Summary") { Source = view });

        var columns = Row();
        string[] slugs = [WorkbenchText.GitBlameLine, WorkbenchText.GitBlameAuthor, WorkbenchText.GitBlameCommit, WorkbenchText.GitBlameSource];
        for (var column = 0; column < slugs.Length; column++)
        {
            var label = new TextBlock { FontWeight = FontWeight.SemiBold };
            var slug = slugs[column];
            localization.Observe(label, () => label.Text = localization.Text(slug));
            Put(columns, label, column);
        }
        var lines = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GitBlameOperations.Line>((line, _) => line is null ? null : CreateLine(line, localization)),
        };
        AutomationProperties.SetAutomationId(lines, "git.blame.lines");
        lines.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(view.Blame) + ".Lines") { Source = view });

        var details = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
        AutomationProperties.SetAutomationId(details, "git.blame.details");
        lines.SelectionChanged += (_, _) => details.Text = lines.SelectedItem is GitBlameOperations.Line line
            ? string.Join('\n', new[] { line.Text, line.CommitId, line.AuthorEmail, line.Summary, line.OriginPath }.Where(text => !string.IsNullOrEmpty(text)))
            : null;

        var header = new StackPanel { Spacing = 4, Children = { path, revision, refresh, status, columns } };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(details, Avalonia.Controls.Dock.Bottom);
        var content = new DockPanel { Margin = new Thickness(10), Children = { header, details, lines } };
        content.KeyBindings.AddRange([
            new KeyBinding { Command = view.CloseCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.Close.EffectiveGesture) },
            new KeyBinding { Command = view.RefreshCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.RefreshGitBlame.EffectiveGesture) },
        ]);
        return content;
    }

    private static Grid CreateLine(GitBlameOperations.Line line, WorkbenchLocalization localization)
    {
        var row = Row();
        Put(row, Cell(line.LineNumber.ToString(CultureInfo.CurrentCulture)), 0);
        Put(row, Cell(line.Uncommitted ? null : line.Author), 1);
        var commit = Cell(line.CommitId?[..12]);
        if (line.Uncommitted)
            localization.Observe(commit, () => commit.Text = localization.Text(WorkbenchText.GitBlameUncommitted));
        Put(row, commit, 2);
        var source = Cell(line.Text);
        source.FontFamily = new FontFamily("monospace");
        Put(row, source, 3);
        return row;
    }

    private static Grid Row() => new() { ColumnDefinitions = new ColumnDefinitions("55,132,116,*"), ColumnSpacing = 8 };
    private static TextBlock Cell(string? text) => new() { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };

    private static void Put(Grid row, Control control, int column)
    {
        Grid.SetColumn(control, column);
        row.Children.Add(control);
    }
}
