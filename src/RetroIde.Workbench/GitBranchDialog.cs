using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RetroIde.Workbench;

internal sealed class GitBranchDialog : Window
{
    internal sealed record Choice(string Name, bool Switch);
    internal sealed record Prompt(string Target, string? ExistingName = null, bool Delete = false);

    internal GitBranchDialog(WorkbenchLocalization localization, Prompt prompt)
    {
        var descriptor = prompt.Delete ? WorkbenchCommands.DeleteGitBranch :
            prompt.ExistingName is null ? WorkbenchCommands.CreateGitBranch : WorkbenchCommands.RenameGitBranch;
        var slug = descriptor.LabelSlug;
        localization.Observe(this, () => Title = localization.Text(slug));
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var caption = new TextBlock { IsVisible = !prompt.Delete };
        var name = new TextBox { Text = prompt.ExistingName, IsVisible = !prompt.Delete };
        AutomationProperties.SetAutomationId(name, "git.branch.name");
        localization.Observe(caption, () =>
        {
            caption.Text = localization.Text(WorkbenchText.GitBranchName);
            AutomationProperties.SetName(name, caption.Text);
        });
        var checkout = new CheckBox { IsVisible = !prompt.Delete && prompt.ExistingName is null };
        AutomationProperties.SetAutomationId(checkout, "git.branch.checkout");
        localization.Observe(checkout, () => checkout.Content = localization.Text(WorkbenchText.GitBranchCheckout));
        var submit = new Button { IsDefault = !prompt.Delete, IsEnabled = prompt.Delete };
        AutomationProperties.SetAutomationId(submit, "git.branch.submit");
        localization.Observe(submit, () => submit.Content = localization.Text(slug));
        name.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                submit.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) &&
                    !string.Equals(prompt.ExistingName, name.Text, StringComparison.Ordinal);
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
                new TextBlock { Text = prompt.Target, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                caption, name, checkout,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { submit, cancel } },
            },
        };
        Opened += (_, _) =>
        {
            if (prompt.Delete)
                cancel.Focus();
            else
                name.Focus();
        };
    }
}
