using System.Windows.Input;
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
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (descriptor, command) in FileActions(view))
        {
            var button = CommandPresentation.CreateButton(descriptor, command, localization);
            AutomationProperties.SetAutomationId(button, descriptor.Id + ".pane");
            actions.Children.Add(button);
        }

        var path = BoundText("git.selection.path", "Repository.WorktreePath", view);
        path.FontWeight = FontWeight.SemiBold;
        var branch = BoundText("git.selection.branch", "Status.Branch", view);
        var error = BoundText("git.status.error", nameof(view.ErrorMessage), view);
        var prompt = new TextBlock { TextWrapping = TextWrapping.Wrap };
        prompt.Bind(Visual.IsVisibleProperty, new Binding("!HasRepository") { Source = view });
        localization.Observe(prompt, () => prompt.Text = localization.Text(WorkbenchText.PaneDescription(SourceControlViewModel.PaneId)));

        var staged = CreateGroup(view, "git.status.staged", WorkbenchText.GitStaged, nameof(view.Staged));
        var working = CreateGroup(view, "git.status.working", WorkbenchText.GitWorking, nameof(view.Working));
        var conflicts = CreateGroup(view, "git.status.conflicts", WorkbenchText.GitConflicts, nameof(view.Conflicts));
        BindFileSelection(view, staged.List, working.List, conflicts.List);

        var groups = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                path,
                branch,
                actions,
                CreateRecovery(view),
                staged.Panel,
                working.Panel,
                conflicts.Panel,
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
            }.Concat(FileActions(view).Concat(RecoveryActions(view))
                .Select(action => CommandPresentation.CreateMenuItem(action.Descriptor, action.Command, view.Localization))).ToArray(),
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
        foreach (var (descriptor, command) in FileActions(view).Concat(RecoveryActions(view)))
            WorkbenchWindow.RegisterShortcut(window, command, CommandPresentation.Create(descriptor).Gesture);
    }

    private static (CommandDescriptor Descriptor, ICommand Command)[] FileActions(SourceControlViewModel view) =>
        [(WorkbenchCommands.OpenGitDiff, view.OpenDiffCommand), (WorkbenchCommands.OpenGitBlame, view.OpenBlameCommand),
            (WorkbenchCommands.StageGitFile, view.StageFileCommand),
            (WorkbenchCommands.UnstageGitFile, view.UnstageFileCommand), (WorkbenchCommands.CreateCommit, view.CommitCommand),
            (WorkbenchCommands.AmendCommit, view.AmendCommand), (WorkbenchCommands.OpenGitHistory, view.OpenHistoryCommand),
            (WorkbenchCommands.OpenGitStashes, view.OpenStashesCommand), (WorkbenchCommands.OpenGitPatches, view.OpenPatchesCommand)];

    private static (CommandDescriptor Descriptor, ICommand Command)[] RecoveryActions(SourceControlViewModel view) =>
        [(WorkbenchCommands.ContinueGitOperation, view.ContinueOperationCommand), (WorkbenchCommands.SkipGitOperation, view.SkipOperationCommand),
            (WorkbenchCommands.AbortGitOperation, view.AbortOperationCommand)];

    private static Control CreateRecovery(SourceControlViewModel view)
    {
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(title, "git.operation.status");
        var buttons = new WrapPanel();
        foreach (var (descriptor, command) in RecoveryActions(view))
            buttons.Children.Add(CommandPresentation.CreateButton(descriptor, command, view.Localization));
        var group = new StackPanel { Spacing = 4, Children = { title, buttons } };
        void Update()
        {
            var kind = view.OperationKind;
            title.Text = kind is null ? null : WorkbenchText.GitOperation(view.Localization, kind);
            group.IsVisible = kind is not null;
            buttons.IsVisible = GitOperationRecovery.IsSupported(kind);
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(view.OperationKind), StringComparison.Ordinal))
                Update();
        };
        group.AttachedToVisualTree += (_, _) =>
        {
            view.PropertyChanged += changed;
            Update();
        };
        group.DetachedFromVisualTree += (_, _) => view.PropertyChanged -= changed;
        view.Localization.Observe(group, Update);
        return group;
    }

    private static void BindFileSelection(SourceControlViewModel view, ListBox staged, ListBox working, ListBox conflicts)
    {
        ListBox[] lists = [staged, working, conflicts];
        foreach (var list in lists)
        {
            var layer = ReferenceEquals(staged, list) ? "staged" : "working";
            list.SelectionChanged += (_, args) =>
            {
                if (args.AddedItems.Count == 0 || list.SelectedItem is not GitStatusEntry entry)
                {
                    if (args.RemovedItems.Count > 0 && args.RemovedItems[0] is GitStatusEntry removed)
                        view.ClearFile(removed, layer);
                    return;
                }
                foreach (var other in lists)
                    if (!ReferenceEquals(other, list))
                        other.SelectedItem = null;
                view.SelectFile(entry, layer);
            };
            list.DoubleTapped += (_, _) =>
            {
                if (list.SelectedItem is GitStatusEntry && view.OpenDiffCommand.CanExecute(null))
                    view.OpenDiffCommand.Execute(null);
            };
        }
    }

    private static TextBlock BoundText(string id, string path, SourceControlViewModel view)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(label, id);
        label.Bind(TextBlock.TextProperty, new Binding(path) { Source = view, FallbackValue = string.Empty, TargetNullValue = string.Empty });
        return label;
    }

    private static (Control Panel, ListBox List) CreateGroup(SourceControlViewModel view, string id, string slug, string property)
    {
        var header = new TextBlock { FontWeight = FontWeight.SemiBold };
        view.Localization.Observe(header, () => header.Text = view.Localization.Text(slug));
        var list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GitStatusEntry>((entry, _) => new TextBlock
            {
                Text = entry?.OriginalPath is { } previous ? $"{previous} → {entry.Path}" : entry?.Path,
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        AutomationProperties.SetAutomationId(list, id);
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(property) { Source = view });
        return (new StackPanel { Spacing = 3, Children = { header, list } }, list);
    }
}
