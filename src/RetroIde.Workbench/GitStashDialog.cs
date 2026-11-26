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

internal sealed class GitStashDialog : Window
{
    internal GitStashDialog(SourceControlViewModel view)
    {
        Width = 790;
        Height = 650;
        MinWidth = 480;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        view.Localization.Observe(this, () => Title = view.Localization.Text(WorkbenchText.GitStashTitle));
        var entries = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GitStash.Entry>((entry, _) => entry is null ? null :
                new TextBlock { Text = $"{entry.Selector}  {entry.Message}", TextWrapping = TextWrapping.Wrap }),
        };
        AutomationProperties.SetAutomationId(entries, "git.stash.entries");
        entries.Bind(ItemsControl.ItemsSourceProperty, Bind("Stashes.Entries"));
        entries.Bind(ListBox.SelectedItemProperty, Bind(nameof(view.SelectedStash), BindingMode.TwoWay));
        var patch = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("monospace") };
        AutomationProperties.SetAutomationId(patch, "git.stash.patch");
        patch.Bind(TextBox.TextProperty, Bind(nameof(view.StashPatch)));
        var close = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(close, "git.stash.close");
        view.Localization.Observe(close, () => close.Content = view.Localization.Text(WorkbenchText.GitRemoteClose));
        close.Click += (_, _) => Close();
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        result.Bind(TextBlock.TextProperty, Bind("LastResult.Summary"));
        var path = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        path.Bind(TextBlock.TextProperty, Bind("Stashes.Repository.WorktreePath"));
        var grid = new Grid { Margin = new Thickness(14), RowSpacing = 7, RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto") };
        Add(path, 0);
        Add(entries, 1);
        Add(CreateActions(view, Register), 2);
        Add(patch, 3);
        Add(new StackPanel { Spacing = 5, Children = { result, close } }, 4);
        Content = view.WrapStashes(grid);

        Binding Bind(string name, BindingMode mode = BindingMode.OneWay) => new(name) { Source = view, Mode = mode };
        void Add(Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }
        Button Register(CommandDescriptor descriptor, ICommand command)
        {
            KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
            return CommandPresentation.CreateButton(descriptor, command, view.Localization);
        }
    }

    private static Control CreateActions(SourceControlViewModel view, Func<CommandDescriptor, ICommand, Button> button)
    {
        var message = new TextBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(message, "git.stash.message");
        view.Localization.Observe(message, () =>
        {
            message.PlaceholderText = view.Localization.Text(WorkbenchText.GitStashMessage);
            AutomationProperties.SetName(message, message.PlaceholderText);
        });
        message.Bind(TextBox.TextProperty, new Binding(nameof(view.StashMessage)) { Source = view, Mode = BindingMode.TwoWay });
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new WrapPanel { Children = { message, Check(WorkbenchText.GitStashUntracked, nameof(view.IncludeUntrackedInStash), "git.stash.untracked"),
                    button(WorkbenchCommands.CreateGitStash, view.CreateStashCommand) } },
                new WrapPanel { Children = { button(WorkbenchCommands.RefreshGitStashes, view.RefreshStashesCommand),
                    button(WorkbenchCommands.InspectGitStash, view.InspectStashCommand),
                    Check(WorkbenchText.GitStashIndex, nameof(view.RestoreStashIndex), "git.stash.index"),
                    button(WorkbenchCommands.ApplyGitStash, view.ApplyStashCommand), button(WorkbenchCommands.PopGitStash, view.PopStashCommand),
                    button(WorkbenchCommands.DropGitStash, view.DropStashCommand) } },
            },
        };

        CheckBox Check(string slug, string property, string id)
        {
            var box = new CheckBox();
            AutomationProperties.SetAutomationId(box, id);
            view.Localization.Observe(box, () => box.Content = view.Localization.Text(slug));
            box.Bind(CheckBox.IsCheckedProperty, new Binding(property) { Source = view, Mode = BindingMode.TwoWay });
            return box;
        }
    }
}
