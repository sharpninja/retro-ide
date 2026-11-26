using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitResetDialog : Window
{
    internal sealed record Choice(string Mode);

    internal GitResetDialog(WorkbenchLocalization localization, GitReset.Review review)
    {
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localization.Observe(this, () => Title = localization.Text(WorkbenchCommands.ResetToGitCommit.LabelSlug));
        var branch = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Observe(branch, () => branch.Text = review.Branch is "(detached)"
            ? localization.Text(WorkbenchText.GitDetachedHead) : review.Branch);
        var mode = CreateModePicker(localization);
        var effects = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(effects, "git.reset.effect");
        var index = CreatePaths(localization, WorkbenchText.GitResetIndexPaths, review.IndexChanges);
        var files = CreatePaths(localization, WorkbenchText.GitResetTrackedPaths, review.HardTrackedChanges);
        var blocked = CreatePaths(localization, WorkbenchText.GitResetBlockedPaths, review.HardResetBlockedPaths);
        var reset = new Button();
        AutomationProperties.SetAutomationId(reset, "git.reset.submit");
        localization.Observe(reset, () => reset.Content = localization.Text(WorkbenchText.GitResetApply));
        reset.Click += (_, _) =>
        {
            if (mode.SelectedItem is ComboBoxItem { Tag: string selected } && reset.IsEnabled)
                Close(new Choice(selected));
        };
        var cancel = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "git.reset.cancel");
        localization.Observe(cancel, () => cancel.Content = localization.Text(WorkbenchText.GitSetupCancel));
        cancel.Click += (_, _) => Close();
        mode.SelectionChanged += (_, _) => UpdateMode();
        localization.Observe(effects, UpdateMode);

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 7,
            Children =
            {
                Caption(localization, WorkbenchText.GitStartCheckout), Value(review.Repository.WorktreePath), branch, Value(review.Head),
                Caption(localization, WorkbenchText.GitStartTarget), Value(review.TargetCommit),
                Caption(localization, WorkbenchText.GitResetMode), mode, effects, index, files, blocked,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { reset, cancel } },
            },
        };
        Opened += (_, _) => mode.Focus();

        void UpdateMode()
        {
            var selected = (mode.SelectedItem as ComboBoxItem)?.Tag as string;
            effects.Text = localization.Text(selected switch
            {
                "soft" => WorkbenchText.GitResetSoftEffect,
                "hard" => WorkbenchText.GitResetHardEffect,
                _ => WorkbenchText.GitResetMixedEffect,
            });
            index.IsVisible = selected is "mixed" or "hard" && review.IndexChanges.Length > 0;
            files.IsVisible = selected is "hard" && review.HardTrackedChanges.Length > 0;
            blocked.IsVisible = selected is "hard" && review.HardResetBlockedPaths.Length > 0;
            reset.IsEnabled = selected is not null && !blocked.IsVisible;
        }

    }

    private static ComboBox CreateModePicker(WorkbenchLocalization localization)
    {
        var mode = new ComboBox
        {
            ItemsSource = new[]
            {
                Option("soft", WorkbenchText.GitResetSoft),
                Option("mixed", WorkbenchText.GitResetMixed),
                Option("hard", WorkbenchText.GitResetHard),
            },
            SelectedIndex = 1,
        };
        AutomationProperties.SetAutomationId(mode, "git.reset.mode");
        localization.Observe(mode, () => AutomationProperties.SetName(mode, localization.Text(WorkbenchText.GitResetMode)));
        return mode;

        ComboBoxItem Option(string value, string slug)
        {
            var item = new ComboBoxItem { Tag = value };
            localization.Observe(item, () => item.Content = localization.Text(slug));
            return item;
        }

    }

    private static StackPanel CreatePaths(WorkbenchLocalization localization, string title, string[] paths) => new()
    {
        Children = { Caption(localization, title), new ScrollViewer { MaxHeight = 110, Content = Value(string.Join('\n', paths)) } },
    };

    private static TextBlock Caption(WorkbenchLocalization localization, string slug)
    {
        var text = new TextBlock { FontWeight = FontWeight.SemiBold };
        localization.Observe(text, () => text.Text = localization.Text(slug));
        return text;
    }

    private static SelectableTextBlock Value(string? value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
}
