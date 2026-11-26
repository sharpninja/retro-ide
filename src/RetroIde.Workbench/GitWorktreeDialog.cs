using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitWorktreeDialog : Window
{
    internal sealed record Prompt(CommandDescriptor Command, string Head, GitHistoryOperations.Reference? ExistingBranch = null,
        GitHistoryOperations.Worktree? Target = null);

    internal sealed record Choice(string? Text = null, string? NewBranch = null, bool UseExistingBranch = false);

    internal GitWorktreeDialog(WorkbenchLocalization localization, Prompt prompt)
    {
        localization.Observe(this, () => Title = localization.Text(prompt.Command.LabelSlug));
        Width = 580;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fields = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        var submit = CreateButton(localization, "git.worktree.submit", prompt.Command.LabelSlug);
        var cancel = CreateButton(localization, "git.worktree.cancel", WorkbenchText.GitSetupCancel);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        if (prompt.Target is { } target)
            AddReviewFields(fields, localization, prompt, target, submit, cancel);
        else
            AddCreationFields(fields, localization, prompt, submit);
        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { submit, cancel },
        });
        Content = fields;
    }

    private void AddCreationFields(Panel fields, WorkbenchLocalization localization, Prompt prompt, Button submit)
    {
        var revision = new SelectableTextBlock { Text = prompt.Head, TextWrapping = TextWrapping.Wrap };
        fields.Children.Add(revision);
        var path = AddInput(fields, localization, "git.worktree.destination", WorkbenchText.GitSetupDestination);
        var branch = AddInput(fields, localization, "git.worktree.branch", WorkbenchText.GitWorktreeNewBranch);
        if (prompt.ExistingBranch is { } source)
        {
            var existing = new CheckBox();
            AutomationProperties.SetAutomationId(existing, "git.worktree.existing");
            var name = source.Name["refs/heads/".Length..];
            localization.Observe(existing, () => existing.Content = $"{localization.Text(WorkbenchText.GitWorktreeExistingBranch)} {name}");
            existing.PropertyChanged += (_, args) =>
            {
                if (args.Property != CheckBox.IsCheckedProperty)
                    return;
                branch.IsEnabled = existing.IsChecked != true;
                revision.Text = existing.IsChecked == true ? source.ObjectId : prompt.Head;
            };
            fields.Children.Insert(3, existing);
            submit.Click += (_, _) => ConfirmCreation(existing.IsChecked == true);
        }
        else
            submit.Click += (_, _) => ConfirmCreation(false);
        submit.IsDefault = true;
        submit.IsEnabled = false;
        path.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                submit.IsEnabled = !string.IsNullOrWhiteSpace(path.Text);
        };
        Opened += (_, _) => path.Focus();

        void ConfirmCreation(bool existing)
        {
            if (!string.IsNullOrWhiteSpace(path.Text))
                Close(new Choice(path.Text, existing || string.IsNullOrWhiteSpace(branch.Text) ? null : branch.Text, existing));
        }
    }

    private void AddReviewFields(Panel fields, WorkbenchLocalization localization, Prompt prompt,
        GitHistoryOperations.Worktree target, Button submit, Button cancel)
    {
        fields.Children.Add(new SelectableTextBlock { Text = target.Path, TextWrapping = TextWrapping.Wrap });
        var branch = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Observe(branch, () => branch.Text = target.Branch ?? localization.Text(WorkbenchText.GitDetachedHead));
        fields.Children.Add(branch);
        fields.Children.Add(new SelectableTextBlock { Text = prompt.Head, TextWrapping = TextWrapping.Wrap });
        var locking = prompt.Command == WorkbenchCommands.LockGitWorktree;
        var reason = locking ? AddInput(fields, localization, "git.worktree.reason", WorkbenchText.GitWorktreeLockReason) : null;
        if (!locking)
        {
            var confirmation = new TextBlock { TextWrapping = TextWrapping.Wrap };
            localization.Observe(confirmation, () => confirmation.Text = localization.Text(WorkbenchText.GitWorktreeRemoveConfirmation));
            fields.Children.Add(confirmation);
        }
        submit.IsDefault = locking;
        submit.Click += (_, _) => Close(new Choice(reason?.Text));
        Opened += (_, _) =>
        {
            if (reason is not null)
                reason.Focus();
            else
                cancel.Focus();
        };
    }

    private static TextBox AddInput(Panel fields, WorkbenchLocalization localization, string id, string slug)
    {
        var label = new TextBlock();
        var input = new TextBox();
        AutomationProperties.SetAutomationId(input, id);
        localization.Observe(label, () =>
        {
            label.Text = localization.Text(slug);
            AutomationProperties.SetName(input, label.Text);
        });
        fields.Children.Add(label);
        fields.Children.Add(input);
        return input;
    }

    private static Button CreateButton(WorkbenchLocalization localization, string id, string slug)
    {
        var button = new Button();
        AutomationProperties.SetAutomationId(button, id);
        localization.Observe(button, () => button.Content = localization.Text(slug));
        return button;
    }
}
