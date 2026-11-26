using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal static class GitHistorySurface
{
    internal static Control Create(GitHistoryViewModel view)
    {
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("265,*"),
            RowDefinitions = new RowDefinitions("Auto,*,205"),
            Margin = new Thickness(8), ColumnSpacing = 8, RowSpacing = 8,
        };
        AutomationProperties.SetAutomationId(layout, "git.history.surface");
        var path = new TextBlock { Text = view.Page.Repository.WorktreePath, TextTrimming = TextTrimming.CharacterEllipsis };
        var reference = new TextBlock { FontWeight = FontWeight.SemiBold };
        reference.Bind(TextBlock.TextProperty, new Binding("Page.RefName") { Source = view, TargetNullValue = "HEAD" });
        var toolbar = new WrapPanel { Children = { Button(WorkbenchCommands.CurrentGitHistory, view.CurrentHistoryCommand),
            Button(WorkbenchCommands.PreviousGitHistoryPage, view.PreviousPageCommand),
            Button(WorkbenchCommands.NextGitHistoryPage, view.NextPageCommand) } };
        var header = new StackPanel { Children = { path, reference, toolbar } };
        Grid.SetColumnSpan(header, 2);
        layout.Children.Add(header);

        var refPane = CreateRefPane(view, Button(WorkbenchCommands.ShowGitRefHistory, view.ShowReferenceCommand));
        Place(refPane, 1, 0);
        var worktrees = CreateList<GitHistoryOperations.Worktree>(view, "git.history.worktrees", "References.Worktrees", worktree =>
            new StackPanel
            {
                Children = { new TextBlock { Text = worktree.Branch ?? worktree.Head, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = worktree.Path, TextTrimming = TextTrimming.CharacterEllipsis } },
            });
        Place(Section(view, WorkbenchText.GitHistoryWorktrees, worktrees), 2, 0);

        var graph = CreateList<GitHistoryGraph.Row>(view, "git.history.graph", nameof(view.Rows), BuildGraphRow);
        graph.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(Layoutable.MarginProperty, new Thickness(0)) },
        });
        graph.Bind(ListBox.SelectedItemProperty, new Binding(nameof(view.SelectedCommit)) { Source = view, Mode = BindingMode.TwoWay });
        graph.SelectionChanged += (_, _) =>
        {
            view.SelectedCommit = graph.SelectedItem as GitHistoryGraph.Row;
            if (view.InspectCommand.CanExecute(null))
                view.InspectCommand.Execute(null);
        };
        Place(Section(view, WorkbenchText.GitHistoryGraph, graph), 1, 1);
        Place(CreateDetails(view, Button(WorkbenchCommands.InspectGitCommit, view.InspectCommand)), 2, 1);
        layout.KeyBindings.Add(new KeyBinding { Command = view.CloseCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.Close.EffectiveGesture) });
        return layout;

        Button Button(CommandDescriptor descriptor, ICommand command)
        {
            layout.KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
            return CommandPresentation.CreateButton(descriptor, command, view.Localization);
        }

        void Place(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            layout.Children.Add(control);
        }
    }

    private static Control CreateRefPane(GitHistoryViewModel view, Button open)
    {
        var refs = CreateList<GitHistoryOperations.Reference>(view, "git.history.refs", "References.Refs", reference =>
            new StackPanel
            {
                Children = { new TextBlock { Text = reference.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = reference.Upstream ?? reference.ObjectId, TextTrimming = TextTrimming.CharacterEllipsis } },
            });
        refs.Bind(ListBox.SelectedItemProperty, new Binding(nameof(view.SelectedReference)) { Source = view, Mode = BindingMode.TwoWay });
        return Section(view, WorkbenchText.GitHistoryRefs, refs, open);
    }

    private static ListBox CreateList<T>(GitHistoryViewModel view, string id, string path, Func<T, Control> template) where T : class
    {
        var list = new ListBox { ItemTemplate = new FuncDataTemplate<T>((value, _) => value is null ? null : template(value)) };
        AutomationProperties.SetAutomationId(list, id);
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(path) { Source = view });
        return list;
    }

    private static Control Section(GitHistoryViewModel view, string slug, Control content, Control? command = null)
    {
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        view.Localization.Observe(title, () => title.Text = view.Localization.Text(slug));
        var stack = new StackPanel { Children = { title } };
        if (command is not null)
            stack.Children.Add(command);
        DockPanel.SetDock(stack, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { stack, content } };
    }

    private static Control BuildGraphRow(GitHistoryGraph.Row row)
    {
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        summary.Children.Add(new GitHistoryGraph.Cell(row) { Width = row.Width, Height = 27 });
        summary.Children.Add(new TextBlock { Text = row.Commit.Id[..Math.Min(8, row.Commit.Id.Length)], VerticalAlignment = VerticalAlignment.Center });
        summary.Children.Add(new TextBlock { Text = row.Commit.Subject, VerticalAlignment = VerticalAlignment.Center });
        if (row.Commit.Refs.Length > 0)
            summary.Children.Add(new TextBlock { Text = string.Join(" · ", row.Commit.Refs), Foreground = Brushes.LightSkyBlue, VerticalAlignment = VerticalAlignment.Center });
        return summary;
    }

    private static Control CreateDetails(GitHistoryViewModel view, Button inspect)
    {
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(message, "git.history.commit-message");
        message.Bind(TextBlock.TextProperty, new Binding("Details.Message") { Source = view });
        var id = Bound("Details.Commit.Id");
        var author = Bound("Details.Commit.Author");
        var date = Bound("Details.Commit.AuthoredAt");
        var changes = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<GitHistoryOperations.ChangedPath>((entry, _) => new TextBlock
            {
                Text = entry?.OriginalPath is { } previous ? $"{entry.Status}  {previous} → {entry.Path}" :
                    entry is null ? "" : $"{entry.Status}  {entry.Path}",
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        AutomationProperties.SetAutomationId(changes, "git.history.changed-files");
        changes.Bind(ItemsControl.ItemsSourceProperty, new Binding("Details.Changes") { Source = view });
        var body = new StackPanel { Spacing = 4, Children = { id, author, date, message, changes, Bound("LastResult.Summary") } };
        DockPanel.SetDock(inspect, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { inspect, new ScrollViewer { Content = body } } };

        TextBlock Bound(string path)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, new Binding(path) { Source = view });
            return text;
        }
    }
}
