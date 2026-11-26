using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Builds the dockable Source Control surface and its shared Git menu bindings.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Keep menu, shortcuts and pane buttons on one command instance.</remarks>
internal static class SourceControlPane
{
    internal static Control Create(SourceControlViewModel view)
    {
        var localization = view.Localization;
        var select = CommandPresentation.CreateButton(WorkbenchCommands.SelectRepository, view.SelectRepositoryCommand, localization);
        AutomationProperties.SetAutomationId(select, WorkbenchCommands.SelectRepository.Id + ".pane");
        var refresh = CommandPresentation.CreateButton(WorkbenchCommands.RefreshGitStatus, view.RefreshStatusCommand, localization);
        AutomationProperties.SetAutomationId(refresh, WorkbenchCommands.RefreshGitStatus.Id + ".pane");

        var path = BoundText("git.selection.path", "Repository.WorktreePath", view);
        path.FontWeight = FontWeight.SemiBold;
        var branch = BoundText("git.selection.branch", "Status.Branch", view);
        var error = BoundText("git.status.error", nameof(view.ErrorMessage), view);
        var prompt = new TextBlock { TextWrapping = TextWrapping.Wrap };
        prompt.Bind(Visual.IsVisibleProperty, new Binding("!HasRepository") { Source = view });
        localization.Observe(prompt, () => prompt.Text = localization.Text(WorkbenchText.PaneDescription(SourceControlViewModel.PaneId)));

        var groups = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                path,
                branch,
                CreateGroup(view, "git.status.staged", WorkbenchText.GitStaged, nameof(view.Staged)),
                CreateGroup(view, "git.status.working", WorkbenchText.GitWorking, nameof(view.Working)),
                CreateGroup(view, "git.status.conflicts", WorkbenchText.GitConflicts, nameof(view.Conflicts)),
            },
        };
        groups.Bind(Visual.IsVisibleProperty, new Binding(nameof(view.HasRepository)) { Source = view });
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(8),
                Spacing = 8,
                Children = { new WrapPanel { Orientation = Orientation.Horizontal, Children = { select, refresh } }, prompt, error, groups },
            },
        };
    }

    internal static MenuItem CreateMenu(SourceControlViewModel view)
    {
        var menu = new MenuItem
        {
            ItemsSource = new[]
            {
                CommandPresentation.CreateMenuItem(WorkbenchCommands.OpenSourceControl, view.OpenPaneCommand, view.Localization),
                CommandPresentation.CreateMenuItem(WorkbenchCommands.InitializeRepository, view.InitializeRepositoryCommand, view.Localization),
                CommandPresentation.CreateMenuItem(WorkbenchCommands.CloneRepository, view.CloneRepositoryCommand, view.Localization),
                CommandPresentation.CreateMenuItem(WorkbenchCommands.SelectRepository, view.SelectRepositoryCommand, view.Localization),
                CommandPresentation.CreateMenuItem(WorkbenchCommands.RefreshGitStatus, view.RefreshStatusCommand, view.Localization),
            },
        };
        AutomationProperties.SetAutomationId(menu, WorkbenchText.GitMenu);
        view.Localization.Observe(menu, () => menu.Header = view.Localization.Text(WorkbenchText.GitMenu));
        return menu;
    }

    internal static void RegisterShortcuts(Window window, SourceControlViewModel view)
    {
        WorkbenchWindow.RegisterShortcut(window, view.OpenPaneCommand, CommandPresentation.Create(WorkbenchCommands.OpenSourceControl).Gesture);
        WorkbenchWindow.RegisterShortcut(window, view.InitializeRepositoryCommand, CommandPresentation.Create(WorkbenchCommands.InitializeRepository).Gesture);
        WorkbenchWindow.RegisterShortcut(window, view.CloneRepositoryCommand, CommandPresentation.Create(WorkbenchCommands.CloneRepository).Gesture);
        WorkbenchWindow.RegisterShortcut(window, view.SelectRepositoryCommand, CommandPresentation.Create(WorkbenchCommands.SelectRepository).Gesture);
        WorkbenchWindow.RegisterShortcut(window, view.RefreshStatusCommand, CommandPresentation.Create(WorkbenchCommands.RefreshGitStatus).Gesture);
    }

    private static TextBlock BoundText(string id, string path, SourceControlViewModel view)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(label, id);
        label.Bind(TextBlock.TextProperty, new Binding(path) { Source = view, FallbackValue = string.Empty, TargetNullValue = string.Empty });
        return label;
    }

    private static Control CreateGroup(SourceControlViewModel view, string id, string slug, string property)
    {
        var header = new TextBlock { FontWeight = FontWeight.SemiBold };
        view.Localization.Observe(header, () => header.Text = view.Localization.Text(slug));
        var list = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<GitStatusEntry>((entry, _) => new TextBlock
            {
                Text = entry?.OriginalPath is { } previous ? $"{previous} → {entry.Path}" : entry?.Path,
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        AutomationProperties.SetAutomationId(list, id);
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(property) { Source = view });
        return new StackPanel { Spacing = 3, Children = { header, list } };
    }
}
