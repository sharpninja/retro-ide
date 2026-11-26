using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Presents the reviewed checkout and collects the message for a commit or an amend.</summary>
internal sealed class GitCommitDialog : Window
{
    internal GitCommitDialog(WorkbenchLocalization localization, GitCommitOperations.CommitState state, bool amend)
    {
        var action = amend ? WorkbenchCommands.AmendCommit.LabelSlug : WorkbenchCommands.CreateCommit.LabelSlug;
        localization.Observe(this, () => Title = localization.Text(action));
        Width = 560;
        MinWidth = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var caption = new TextBlock();
        var message = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 130, Text = amend ? state.Message : "" };
        AutomationProperties.SetAutomationId(message, "git.commit.message");
        localization.Observe(caption, () =>
        {
            caption.Text = localization.Text(WorkbenchText.GitCommitMessage);
            AutomationProperties.SetName(message, caption.Text);
        });
        var submit = new Button();
        AutomationProperties.SetAutomationId(submit, "git.commit.submit");
        localization.Observe(submit, () => submit.Content = localization.Text(action));
        var canCommit = amend ? state.Head is not null : state.HasStagedChanges;
        message.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                UpdateSubmit();
        };
        UpdateSubmit();
        submit.Click += (_, _) =>
        {
            if (submit.IsEnabled)
                Close(message.Text);
        };
        var cancel = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "git.commit.cancel");
        localization.Observe(cancel, () => cancel.Content = localization.Text(WorkbenchText.GitSetupCancel));
        cancel.Click += (_, _) => Close();
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        body.Children.Add(new TextBlock { Text = state.Repository.WorktreePath, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = state.Branch });
        if (amend || !canCommit)
        {
            var notice = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var slug = (amend, canCommit) switch
            {
                (true, true) => WorkbenchText.GitAmendNotice,
                (true, false) => WorkbenchText.GitNoCommit,
                _ => WorkbenchText.GitNoStagedChanges,
            };
            localization.Observe(notice, () => notice.Text = localization.Text(slug));
            body.Children.Add(notice);
        }
        body.Children.AddRange([caption, message, new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { submit, cancel },
        }]);
        Content = body;
        Opened += (_, _) => message.Focus();

        void UpdateSubmit() => submit.IsEnabled = canCommit && !string.IsNullOrWhiteSpace(message.Text);
    }
}
