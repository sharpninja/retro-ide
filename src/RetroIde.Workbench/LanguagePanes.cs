using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using RetroIde.Application.Workspaces;

using static RetroIde.Workbench.DockPaneControls;

namespace RetroIde.Workbench;

internal static partial class LanguagePanes
{
    internal static Control Create(LanguageServersViewModel view)
    {
        var locale = view.Localization;
        var pane = new StackPanel { Margin = new Thickness(8), Spacing = 8 };
        var profiles = new ListBox
        {
            MaxHeight = 155,
            ItemTemplate = RowTemplate<LanguageServersViewModel.ProfileRow>(row => (row.Title, row.Detail)),
        };
        AutomationProperties.SetAutomationId(profiles, "language.server.profiles");
        locale.Observe(profiles, () => AutomationProperties.SetName(profiles, locale.Text(WorkbenchText.LanguageProfiles)));
        ConnectSelection(pane, profiles, view, () => view.Profiles, () => view.SelectedProfile, row => view.SelectedProfile = row);

        var actions = new WrapPanel
        {
            Children =
            {
                Action(pane, WorkbenchCommands.LanguageRefreshServers, view.RefreshCommand, locale),
                Action(pane, WorkbenchCommands.LanguageStopServer, view.StopCommand, locale),
                Action(pane, WorkbenchCommands.LanguageRestartServer, view.RestartCommand, locale),
                Action(pane, WorkbenchCommands.LanguageRemoveServer, view.RemoveCommand, locale),
                Action(pane, WorkbenchCommands.LanguageServerOutput, view.ReadOutputCommand, locale),
            },
        };
        var output = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        var outputRegion = new ScrollViewer { MaxHeight = 135, Content = output };
        AutomationProperties.SetAutomationId(output, "language.server.stderr");
        Watch(pane, view, () =>
        {
            output.Text = string.IsNullOrEmpty(view.StandardError) ? locale.Text(WorkbenchText.LanguageNoOutput) : view.StandardError;
            outputRegion.IsVisible = view.StandardError is not null;
        });
        locale.Observe(output, () => AutomationProperties.SetName(output, locale.Text(WorkbenchText.LanguageServerOutput)));

        pane.Children.AddRange([ServerEditReviews(pane, view), profiles, actions,
            Status(pane, view), outputRegion, Configuration(pane, view)]);
        return new ScrollViewer { Content = pane };
    }

    private static Control Configuration(Control scope, LanguageServersViewModel view)
    {
        var locale = view.Localization;
        var roots = new ComboBox
        {
            MinWidth = 170,
            ItemTemplate = new FuncDataTemplate<WorkspaceExplorer.Root>((root, _) =>
                new TextBlock { Text = root?.Path, TextTrimming = TextTrimming.CharacterEllipsis }),
        };
        SetLabel(roots, locale, WorkbenchText.LanguageRoot, "language.server.root");
        ConnectSelection(scope, roots, view, () => view.Roots, () => view.SelectedRoot, root => view.SelectedRoot = root);
        var advanced = new Expander
        {
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Field(locale, WorkbenchText.LanguageInitialization, "language.server.initialization-options",
                        view.InitializationOptions, text => view.InitializationOptions = text, multiline: true),
                    Field(locale, WorkbenchText.LanguageSettings, "language.server.settings",
                        view.Settings, text => view.Settings = text, multiline: true),
                },
            },
        };
        AutomationProperties.SetAutomationId(advanced, "language.server.advanced");
        locale.Observe(advanced, () => advanced.Header = locale.Text(WorkbenchText.LanguageAdvanced));
        var form = new Expander
        {
            IsExpanded = true,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Labelled(roots, locale, WorkbenchText.LanguageRoot),
                    Field(locale, WorkbenchText.LanguageProfileId, "language.server.profile-id", view.ProfileId, text => view.ProfileId = text),
                    Field(locale, WorkbenchText.LanguageExecutable, "language.server.executable", view.Executable, text => view.Executable = text),
                    Field(locale, WorkbenchText.LanguageAssociations, "language.server.associations", view.Associations, text => view.Associations = text, multiline: true),
                    Field(locale, WorkbenchText.LanguageArguments, "language.server.arguments", view.Arguments, text => view.Arguments = text),
                    advanced,
                    Action(scope, WorkbenchCommands.LanguageConfigureServer, view.ConfigureCommand, locale),
                },
            },
        };
        locale.Observe(form, () => form.Header = locale.Text(WorkbenchText.LanguageConfiguration));
        return form;
    }

    internal static Control Create(ProblemsViewModel view)
    {
        var locale = view.Localization;
        var pane = new DockPanel { Margin = new Thickness(8) };
        var rows = new ListBox { ItemTemplate = RowTemplate<ProblemsViewModel.ProblemRow>(row => (row.Title, row.Detail)) };
        AutomationProperties.SetAutomationId(rows, "language.problems.items");
        locale.Observe(rows, () => AutomationProperties.SetName(rows, locale.Text(WorkbenchText.PaneTitle(ProblemsViewModel.PaneId))));
        ConnectSelection(pane, rows, view, () => view.Items, () => view.SelectedProblem, row => view.SelectedProblem = row);
        rows.DoubleTapped += (_, _) =>
        {
            if (view.OpenProblemCommand.CanExecute(null))
                view.OpenProblemCommand.Execute(null);
        };
        var empty = new TextBlock();
        var truncated = new TextBlock();
        void UpdateMessages()
        {
            empty.IsVisible = view.Items.Count == 0;
            empty.Text = locale.Text(WorkbenchText.PaneDescription(ProblemsViewModel.PaneId));
            truncated.IsVisible = view.Truncated;
            truncated.Text = locale.Text(WorkbenchText.LanguageProblemLimit);
        }
        locale.Observe(empty, UpdateMessages);
        Watch(pane, view, UpdateMessages);
        var footer = new StackPanel
        {
            Children =
            {
                Status(pane, view), empty, truncated,
                Action(pane, WorkbenchCommands.OpenLanguageProblem, view.OpenProblemCommand, locale),
            },
        };
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        pane.Children.AddRange([footer, rows]);
        return pane;
    }

    private static TextBlock Status(Control anchor, LanguageToolViewModel view)
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Watch(anchor, view, () => status.Text = view.LastResult?.Summary);
        return status;
    }

}
