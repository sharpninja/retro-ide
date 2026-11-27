using System.ComponentModel;
using System.Globalization;
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

internal sealed class GitLfsDialog : Window
{
    internal GitLfsDialog(SourceControlViewModel view)
    {
        Width = 750;
        Height = 510;
        MinWidth = 470;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var localization = view.Localization;
        localization.Observe(this, () => Title = localization.Text(WorkbenchText.GitLfsTitle));

        var repository = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        repository.Bind(TextBlock.TextProperty, Bind("LfsStatus.Repository.WorktreePath"));
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(summary, "git.lfs.status");
        localization.Observe(summary, UpdateStatus);
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName == nameof(view.LfsStatus))
                UpdateStatus();
        };
        view.PropertyChanged += changed;
        Closed += (_, _) => view.PropertyChanged -= changed;

        var inventory = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GitLfsState.FileEntry>((entry, _) =>
                entry is null ? null : CreateRow(localization, entry)),
        };
        AutomationProperties.SetAutomationId(inventory, "git.lfs.files");
        inventory.Bind(ItemsControl.ItemsSourceProperty, Bind("LfsStatus.Files"));
        var remote = new ComboBox { MinWidth = 170 };
        AutomationProperties.SetAutomationId(remote, "git.lfs.remote");
        localization.Observe(remote, () => AutomationProperties.SetName(remote, localization.Text(WorkbenchText.GitLfsRemote)));
        remote.Bind(ItemsControl.ItemsSourceProperty, Bind("LfsStatus.Remotes"));
        remote.Bind(ComboBox.SelectedItemProperty, Bind(nameof(view.LfsRemote), BindingMode.TwoWay));

        var actions = new WrapPanel
        {
            Children =
            {
                remote,
                Register(WorkbenchCommands.RefreshGitLfs, view.RefreshLfsCommand),
                Register(WorkbenchCommands.FetchGitLfs, view.FetchLfsCommand),
                Register(WorkbenchCommands.CheckoutGitLfs, view.CheckoutLfsCommand),
                Register(WorkbenchCommands.PullGitLfs, view.PullLfsCommand),
            },
        };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        result.Bind(TextBlock.TextProperty, Bind("LastResult.Summary"));
        var close = new Button { IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(close, "git.lfs.close");
        localization.Observe(close, () => close.Content = localization.Text(WorkbenchText.GitRemoteClose));
        close.Click += (_, _) => Close();
        var grid = new Grid { Margin = new Thickness(14), RowSpacing = 8, RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        Place(new StackPanel { Spacing = 5, Children = { repository, summary } }, 0);
        Place(inventory, 1);
        Place(actions, 2);
        Place(new StackPanel { Spacing = 5, Children = { result, close } }, 3);
        Content = view.WrapLfs(grid);

        void UpdateStatus()
        {
            summary.Text = view.LfsStatus is { IsInstalled: true } status
                ? $"{status.Version} · {localization.Text(status.HasFilter ? WorkbenchText.GitLfsFilterConfigured : WorkbenchText.GitLfsFilterMissing)}"
                : localization.Text(WorkbenchText.GitLfsUnavailable);
        }
        Binding Bind(string property, BindingMode mode = BindingMode.OneWay) => new(property) { Source = view, Mode = mode };
        void Place(Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }
        Button Register(CommandDescriptor descriptor, ICommand command)
        {
            KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
            return CommandPresentation.CreateButton(descriptor, command, localization);
        }
    }

    private static Control CreateRow(WorkbenchLocalization localization, GitLfsState.FileEntry entry)
    {
        var path = new TextBlock { Text = entry.Path, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var status = entry.IsMaterialized ? WorkbenchText.GitLfsMaterialized :
            entry.IsDownloaded ? WorkbenchText.GitLfsCached : WorkbenchText.GitLfsPointer;
        localization.Observe(details, () => details.Text = $"{localization.Text(status)} · {entry.Size.ToString("N0", CultureInfo.CurrentCulture)} B");
        var oid = new SelectableTextBlock { Text = entry.ObjectId, TextWrapping = TextWrapping.Wrap };
        return new StackPanel { Margin = new Thickness(4), Spacing = 3, Children = { path, details, oid } };
    }
}
