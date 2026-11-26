using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RetroIde.Workbench;

internal sealed class GitBranchDialog : Window
{
    internal sealed record Choice(string Name, bool Switch);

    internal GitBranchDialog(WorkbenchLocalization localization, string start)
    {
        var slug = WorkbenchCommands.CreateGitBranch.LabelSlug;
        localization.Observe(this, () => Title = localization.Text(slug));
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var caption = new TextBlock();
        var name = new TextBox();
        AutomationProperties.SetAutomationId(name, "git.branch.name");
        localization.Observe(caption, () =>
        {
            caption.Text = localization.Text(WorkbenchText.GitBranchName);
            AutomationProperties.SetName(name, caption.Text);
        });
        var checkout = new CheckBox();
        AutomationProperties.SetAutomationId(checkout, "git.branch.checkout");
        localization.Observe(checkout, () => checkout.Content = localization.Text(WorkbenchText.GitBranchCheckout));
        var submit = new Button { IsDefault = true, IsEnabled = false };
        AutomationProperties.SetAutomationId(submit, "git.branch.submit");
        localization.Observe(submit, () => submit.Content = localization.Text(slug));
        name.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                submit.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        };
        submit.Click += (_, _) =>
        {
            if (name.Text is { } text && !string.IsNullOrWhiteSpace(text))
                Close(new Choice(text, checkout.IsChecked == true));
        };
        var cancel = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "git.branch.cancel");
        localization.Observe(cancel, () => cancel.Content = localization.Text(WorkbenchText.GitSetupCancel));
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = start, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                caption, name, checkout,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { submit, cancel } },
            },
        };
        Opened += (_, _) => name.Focus();
    }
}
