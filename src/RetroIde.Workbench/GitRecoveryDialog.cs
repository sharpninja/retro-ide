using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitRecoveryDialog : Window
{
    internal GitRecoveryDialog(WorkbenchLocalization localization, GitOperationRecovery.State state, GitOperationRecovery.Action action)
    {
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var skip = action is GitOperationRecovery.Action.Skip;
        var descriptor = skip ? WorkbenchCommands.SkipGitOperation : WorkbenchCommands.AbortGitOperation;
        var slug = descriptor.LabelSlug;
        localization.Observe(this, () => Title = localization.Text(slug));
        var operation = new TextBlock { FontWeight = FontWeight.SemiBold };
        localization.Observe(operation, () => operation.Text = WorkbenchText.GitOperation(localization, state.Kind));
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Observe(explanation, () => explanation.Text = localization.Text(skip ? WorkbenchText.GitSkipExplanation : WorkbenchText.GitAbortExplanation));
        var pending = new SelectableTextBlock { Text = state.PendingCommit, IsVisible = skip && state.PendingCommit is not null,
            TextWrapping = TextWrapping.Wrap };

        var confirm = new Button();
        AutomationProperties.SetAutomationId(confirm, descriptor.Id + ".confirm");
        localization.Observe(confirm, () => confirm.Content = localization.Text(slug));
        confirm.Click += (_, _) => Close(true);
        var cancel = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, descriptor.Id + ".cancel");
        localization.Observe(cancel, () => cancel.Content = localization.Text(WorkbenchText.GitSetupCancel));
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                operation,
                new SelectableTextBlock { Text = state.Repository.WorktreePath, TextWrapping = TextWrapping.Wrap },
                explanation,
                pending,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { confirm, cancel } },
            },
        };
        Opened += (_, _) => cancel.Focus();
    }
}
