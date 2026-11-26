using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitStartOperationDialog : Window
{
    internal sealed record Choice(string Kind, int? Mainline);

    internal GitStartOperationDialog(WorkbenchLocalization localization, GitHistoryOperations.Worktree checkout,
        GitHistoryOperations.Commit target)
    {
        Width = 610;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localization.Observe(this, () => Title = localization.Text(WorkbenchCommands.StartGitOperation.LabelSlug));

        var checkoutBranch = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Observe(checkoutBranch, () => checkoutBranch.Text = checkout.Branch ?? localization.Text(WorkbenchText.GitDetachedHead));
        var kinds = new ComboBox
        {
            ItemsSource = new[]
            {
                Kind("merge", WorkbenchText.GitStartMerge),
                Kind("rebase", WorkbenchText.GitStartRebase),
                Kind("cherry-pick", WorkbenchText.GitStartCherryPick),
                Kind("revert", WorkbenchText.GitStartRevert),
            },
            SelectedIndex = 0,
        };
        var mainline = new ComboBox { ItemsSource = target.Parents, HorizontalAlignment = HorizontalAlignment.Stretch };
        var mainlineGroup = new StackPanel { Children = { Caption(WorkbenchText.GitMainline, mainline), mainline } };
        AutomationProperties.SetAutomationId(kinds, "git.operation.start.kind");
        AutomationProperties.SetAutomationId(mainline, "git.operation.start.mainline");
        var start = new Button();
        AutomationProperties.SetAutomationId(start, "git.operation.start.submit");
        localization.Observe(start, () => start.Content = localization.Text(WorkbenchText.GitStartApply));
        start.Click += (_, _) =>
        {
            if (kinds.SelectedItem is ComboBoxItem { Tag: string value } && start.IsEnabled)
                Close(new Choice(value, mainlineGroup.IsVisible ? mainline.SelectedIndex + 1 : null));
        };
        var cancel = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "git.operation.start.cancel");
        localization.Observe(cancel, () => cancel.Content = localization.Text(WorkbenchText.GitSetupCancel));
        cancel.Click += (_, _) => Close();
        kinds.SelectionChanged += (_, _) => UpdateChoice();
        mainline.SelectionChanged += (_, _) => UpdateChoice();
        UpdateChoice();

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 7,
            Children =
            {
                Caption(WorkbenchText.GitStartCheckout), Value(checkout.Path), checkoutBranch, Value(checkout.Head),
                Caption(WorkbenchText.GitStartTarget), Value(target.Subject), Value(target.Id),
                Caption(WorkbenchText.GitStartKind, kinds), kinds, mainlineGroup,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { start, cancel } },
            },
        };
        Opened += (_, _) => kinds.Focus();

        void UpdateChoice()
        {
            mainlineGroup.IsVisible = target.Parents.Length > 1 &&
                kinds.SelectedItem is ComboBoxItem { Tag: "cherry-pick" or "revert" };
            start.IsEnabled = kinds.SelectedIndex >= 0 && (!mainlineGroup.IsVisible || mainline.SelectedIndex >= 0);
        }

        ComboBoxItem Kind(string value, string slug)
        {
            var item = new ComboBoxItem { Tag = value };
            localization.Observe(item, () => item.Content = localization.Text(slug));
            return item;
        }

        TextBlock Caption(string slug, Control? field = null)
        {
            var text = new TextBlock { FontWeight = FontWeight.SemiBold };
            localization.Observe(text, () =>
            {
                text.Text = localization.Text(slug);
                if (field is not null)
                    AutomationProperties.SetName(field, text.Text);
            });
            return text;
        }

        static SelectableTextBlock Value(string? value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    }
}
