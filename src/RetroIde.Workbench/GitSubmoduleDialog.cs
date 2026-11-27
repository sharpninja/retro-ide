using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitSubmoduleDialog : Window
{
    internal GitSubmoduleDialog(SourceControlViewModel view)
    {
        Width = 750;
        Height = 490;
        MinWidth = 450;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        view.Localization.Observe(this, () => Title = view.Localization.Text(WorkbenchText.GitSubmodulesTitle));

        var repository = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        repository.Bind(TextBlock.TextProperty, Bind("Repository.WorktreePath"));
        var modules = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GitSubmoduleState.Module>((module, _) =>
                module is null ? null : CreateRow(view.Localization, module)),
        };
        AutomationProperties.SetAutomationId(modules, "git.submodule.entries");
        modules.Bind(ItemsControl.ItemsSourceProperty, Bind(nameof(view.Submodules)));
        modules.Bind(ListBox.SelectedItemProperty, Bind(nameof(view.SelectedSubmodule), BindingMode.TwoWay));
        var empty = new TextBlock { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        empty.Bind(Visual.IsVisibleProperty, Bind(nameof(view.HasNoSubmodules)));
        view.Localization.Observe(empty, () => empty.Text = view.Localization.Text(WorkbenchText.GitSubmoduleEmpty));
        var actions = new WrapPanel
        {
            Children =
            {
                Register(WorkbenchCommands.RefreshGitSubmodules, view.RefreshSubmodulesCommand),
                Register(WorkbenchCommands.InitGitSubmodule, view.InitSubmoduleCommand),
                Register(WorkbenchCommands.UpdateGitSubmodule, view.UpdateSubmoduleCommand),
                Register(WorkbenchCommands.SyncGitSubmodule, view.SyncSubmoduleCommand),
            },
        };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        result.Bind(TextBlock.TextProperty, Bind("LastResult.Summary"));
        var close = new Button { IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(close, "git.submodule.close");
        view.Localization.Observe(close, () => close.Content = view.Localization.Text(WorkbenchText.GitRemoteClose));
        close.Click += (_, _) => Close();

        var content = new Grid { Margin = new Thickness(14), RowSpacing = 8, RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        Place(repository, 0);
        Place(new Grid { Children = { modules, empty } }, 1);
        Place(actions, 2);
        Place(new StackPanel { Spacing = 5, Children = { result, close } }, 3);
        Content = view.WrapSubmodules(content);

        Binding Bind(string property, BindingMode mode = BindingMode.OneWay) => new(property) { Source = view, Mode = mode };
        void Place(Control control, int row)
        {
            Grid.SetRow(control, row);
            content.Children.Add(control);
        }
        Button Register(CommandDescriptor descriptor, ICommand command)
        {
            KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
            return CommandPresentation.CreateButton(descriptor, command, view.Localization);
        }
    }

    private static Control CreateRow(WorkbenchLocalization text, GitSubmoduleState.Module module)
    {
        var path = new TextBlock { Text = module.Path, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var slug = module switch
        {
            { IsConflicted: true } => WorkbenchText.GitSubmoduleConflicted,
            { CanChange: false } => WorkbenchText.GitSubmoduleUnavailable,
            { IsConfigured: false } => WorkbenchText.GitSubmoduleUnconfigured,
            { HasCheckout: false } => WorkbenchText.GitSubmoduleConfigured,
            _ when !string.Equals(module.Head, module.IndexCommit, StringComparison.OrdinalIgnoreCase) => WorkbenchText.GitSubmoduleDifferentHead,
            _ => WorkbenchText.GitSubmoduleCurrent,
        };
        text.Observe(status, () =>
        {
            var state = text.Text(slug);
            status.Text = module.IsDirty ? $"{state} · {text.Text(WorkbenchText.GitSubmoduleDirty)}" : state;
        });
        var row = new StackPanel { Margin = new Thickness(4), Spacing = 3, Children = { path, status } };
        if (module.IndexCommit is { } index)
            row.Children.Add(Commit(WorkbenchText.GitSubmoduleIndexCommit, index));
        if (module.Head is { } head)
            row.Children.Add(Commit(WorkbenchText.GitSubmoduleHead, head));
        return row;

        TextBlock Commit(string label, string value)
        {
            var commit = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Observe(commit, () => commit.Text = $"{text.Text(label)}  {value}");
            return commit;
        }
    }
}
