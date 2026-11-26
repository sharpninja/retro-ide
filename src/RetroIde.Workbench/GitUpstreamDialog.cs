using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitUpstreamDialog : Window
{
    internal sealed record Choice(string? Upstream);

    internal GitUpstreamDialog(WorkbenchLocalization localization, string branch, GitHistoryOperations.Reference[] candidates)
    {
        Width = 520;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localization.Observe(this, () => Title = localization.Text(WorkbenchCommands.SetGitUpstream.LabelSlug));
        var list = new ListBox
        {
            Height = 210, ItemsSource = candidates,
            ItemTemplate = new FuncDataTemplate<GitHistoryOperations.Reference>((reference, _) => new TextBlock { Text = reference?.Name }),
        };
        AutomationProperties.SetAutomationId(list, "git.upstream.refs");
        var submit = Button(WorkbenchCommands.SetGitUpstream.LabelSlug, "git.upstream.submit", () =>
        {
            if (list.SelectedItem is GitHistoryOperations.Reference reference)
                Close(new Choice(reference.Name));
        });
        submit.IsEnabled = false;
        list.SelectionChanged += (_, _) => submit.IsEnabled = list.SelectedItem is not null;
        var clear = Button(WorkbenchText.GitUpstreamClear, "git.upstream.clear", () => Close(new Choice(null)));
        var cancel = Button(WorkbenchText.GitSetupCancel, "git.upstream.cancel", () => Close());
        cancel.IsCancel = true;
        Content = new StackPanel { Margin = new Thickness(14), Spacing = 7,
            Children = { new TextBlock { Text = branch }, list, new WrapPanel { Children = { submit, clear, cancel } } } };

        Button Button(string slug, string id, Action click)
        {
            var button = new Button();
            AutomationProperties.SetAutomationId(button, id);
            localization.Observe(button, () => button.Content = localization.Text(slug));
            button.Click += (_, _) => click();
            return button;
        }
    }
}
