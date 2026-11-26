using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace RetroIde.Workbench;

internal static class ExplorerPane
{
    public static Control Create(ExplorerViewModel view)
    {
        var tools = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { CommandPresentation.CreateButton(WorkbenchCommands.OpenFolder, view.OpenFolderCommand, view.Localization),
                CommandPresentation.CreateButton(WorkbenchCommands.OpenProject, view.OpenProjectCommand, view.Localization) },
        };
        var roots = new TreeView
        {
            ItemsSource = view.TreeRoots,
            ItemTemplate = new FuncTreeDataTemplate<ExplorerTreeNode>((node, _) =>
            {
                var label = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                label.Bind(TextBlock.TextProperty, new Binding(nameof(ExplorerTreeNode.Label)) { Source = node });
                var badge = new TextBlock { Opacity = 0.72, Margin = new Thickness(6, 0, 0, 0) };
                AutomationProperties.SetAutomationId(badge, "workspace.git-status");
                badge.Bind(TextBlock.TextProperty, new Binding(nameof(ExplorerTreeNode.GitBadge)) { Source = node });
                badge.Bind(Visual.IsVisibleProperty, new Binding(nameof(ExplorerTreeNode.HasGitBadge)) { Source = node });
                DockPanel.SetDock(badge, Avalonia.Controls.Dock.Right);
                view.Localization.Observe(label, node.RefreshLabel);
                return new DockPanel { Children = { badge, label } };
            }, node => node.Children),
        };
        roots.Styles.Add(new Style(selector => selector.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(ExplorerTreeNode.IsExpanded)) { Mode = BindingMode.OneWay }),
                new Setter(AutomationProperties.NameProperty, new Binding(nameof(ExplorerTreeNode.Label)) { Mode = BindingMode.OneWay }),
            },
        });
        roots.Bind(TreeView.SelectedItemProperty,
            new Binding(nameof(ExplorerViewModel.SelectedNode)) { Source = view, Mode = BindingMode.OneWay });
        roots.SelectionChanged += (_, args) =>
        {
            if (args.AddedItems.OfType<ExplorerTreeNode>().FirstOrDefault() is { } node && !ReferenceEquals(node, view.SelectedNode))
                view.SelectEntryCommand.Execute(node);
        };
        roots.AddHandler(TreeViewItem.ExpandedEvent, OnExpansionChanged);
        roots.AddHandler(TreeViewItem.CollapsedEvent, OnExpansionChanged);
        roots.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            if (args.ClickCount == 2 && args.GetCurrentPoint(roots).Properties.IsLeftButtonPressed &&
                args.Source is Visual source &&
                (source as TreeViewItem ?? source.FindAncestorOfType<TreeViewItem>())?.DataContext is
                    ExplorerTreeNode { IsDirectory: false, Address: not null } node)
            {
                view.OpenEntryCommand.Execute(node);
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        roots.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Enter && roots.SelectedItem is ExplorerTreeNode { IsDirectory: false, Address: not null } node)
            {
                view.OpenEntryCommand.Execute(node);
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AutomationProperties.SetAutomationId(roots, "workspace.roots");

        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ExplorerViewModel.LastResult) + ".Summary"));
        DockPanel.SetDock(tools, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        return new DockPanel { DataContext = view, Margin = new Thickness(8), Children = { tools, status, roots } };

        void OnExpansionChanged(object? sender, RoutedEventArgs args)
        {
            if (args.Source is TreeViewItem { IsLoaded: true, DataContext: ExplorerTreeNode node } item &&
                item.IsExpanded != node.IsExpanded)
                view.SetExpansionCommand.Execute(new ExplorerViewModel.ExpansionIntent(node, item.IsExpanded));
        }
    }

    public static MenuItem CreateFileMenu(ExplorerViewModel view)
    {
        var menu = new MenuItem
        {
            ItemsSource = new[] { CommandPresentation.CreateMenuItem(WorkbenchCommands.OpenFolder, view.OpenFolderCommand, view.Localization),
                CommandPresentation.CreateMenuItem(WorkbenchCommands.OpenProject, view.OpenProjectCommand, view.Localization) },
        };
        view.Localization.Observe(menu, () => menu.Header = view.Localization.Text(WorkbenchText.FileMenu));
        return menu;
    }

    internal static void RegisterShortcuts(Window window, ExplorerViewModel view)
    {
        WorkbenchWindow.RegisterShortcut(window, view.OpenFolderCommand, CommandPresentation.Create(WorkbenchCommands.OpenFolder).Gesture);
        WorkbenchWindow.RegisterShortcut(window, view.OpenProjectCommand, CommandPresentation.Create(WorkbenchCommands.OpenProject).Gesture);
    }
}
