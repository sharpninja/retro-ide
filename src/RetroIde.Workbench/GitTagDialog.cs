using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitTagDialog : Window
{
    internal sealed record Prompt(string Target, GitTagOperations.Tag? Tag = null, bool Delete = false);
    internal sealed record Choice(string Name, string? Message = null);

    internal GitTagDialog(WorkbenchLocalization localization, Prompt prompt)
    {
        var descriptor = prompt.Delete ? WorkbenchCommands.DeleteGitTag :
            prompt.Tag is null ? WorkbenchCommands.CreateGitTag : WorkbenchCommands.InspectGitTag;
        localization.Observe(this, () => Title = localization.Text(descriptor.LabelSlug));
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fields = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        if (prompt.Tag is { } tag)
        {
            ShowValue(fields, WorkbenchText.GitTagName, tag.TagName, localization);
            ShowValue(fields, WorkbenchText.GitTagObject, tag.ObjectId, localization);
        }
        ShowValue(fields, WorkbenchText.GitTagTarget, prompt.Target, localization);

        var close = Button(localization, "git.tag.cancel", prompt.Tag is not null && !prompt.Delete
            ? WorkbenchText.GitRemoteClose : WorkbenchText.GitSetupCancel);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (prompt.Tag is { } existing)
        {
            if (existing.TaggerName is { } author)
                ShowValue(fields, WorkbenchText.GitTagAuthor, $"{author} <{existing.TaggerEmail}>", localization);
            if (existing.TaggerDate is { } date)
                ShowValue(fields, WorkbenchText.GitTagDate, date, localization);
            AddAnnotation(fields, existing);
            if (prompt.Delete)
            {
                var delete = Button(localization, "git.tag.submit", descriptor.LabelSlug);
                delete.Click += (_, _) => Close(new Choice(existing.TagName));
                actions.Children.Add(delete);
            }
            Opened += (_, _) => close.Focus();
        }
        else
            AddCreation(fields, actions, localization, descriptor.LabelSlug);
        actions.Children.Add(close);
        fields.Children.Add(actions);
        Content = fields;
    }

    private void AddCreation(Panel fields, Panel actions, WorkbenchLocalization localization, string label)
    {
        var name = new TextBox();
        var message = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 200 };
        AddField(fields, name, "git.tag.name", WorkbenchText.GitTagName, localization);
        AddField(fields, message, "git.tag.message", WorkbenchText.GitTagAnnotation, localization);
        var submit = Button(localization, "git.tag.submit", label);
        submit.IsDefault = true;
        submit.IsEnabled = false;
        name.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                submit.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        };
        submit.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text))
                Close(new Choice(name.Text, string.IsNullOrWhiteSpace(message.Text) ? null : message.Text));
        };
        actions.Children.Add(submit);
        Opened += (_, _) => name.Focus();
    }

    private static void ShowValue(Panel fields, string label, string value, WorkbenchLocalization localization)
    {
        var text = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        AddField(fields, text, "", label, localization);
    }

    private static void AddAnnotation(Panel fields, GitTagOperations.Tag tag)
    {
        var text = string.Join("\n\n", new[] { tag.Subject, tag.Body }.Where(value => !string.IsNullOrEmpty(value)));
        if (text.Length == 0)
            return;
        var annotation = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 };
        AutomationProperties.SetAutomationId(annotation, "git.tag.annotation");
        fields.Children.Add(annotation);
    }

    private static void AddField(Panel fields, Control editor, string id, string label, WorkbenchLocalization localization)
    {
        var caption = new TextBlock();
        if (id.Length > 0)
            AutomationProperties.SetAutomationId(editor, id);
        localization.Observe(caption, () =>
        {
            caption.Text = localization.Text(label);
            AutomationProperties.SetName(editor, caption.Text);
        });
        fields.Children.Add(caption);
        fields.Children.Add(editor);
    }

    private static Button Button(WorkbenchLocalization localization, string id, string label)
    {
        var button = new Button();
        AutomationProperties.SetAutomationId(button, id);
        localization.Observe(button, () => button.Content = localization.Text(label));
        return button;
    }
}
