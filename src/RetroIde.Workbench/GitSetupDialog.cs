using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using RetroIde.Application;

namespace RetroIde.Workbench;

/// <summary>Captures the native Git checkout destination and optional clone source.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. The dialog gathers input for the shared command and does no repository I/O.</remarks>
internal sealed class GitSetupDialog : Window
{
    internal sealed record Choice(string Destination, string? Source);

    internal GitSetupDialog(WorkbenchLocalization localization, bool clone, ILogger logger)
    {
        var action = clone ? WorkbenchText.GitCloneAction : WorkbenchText.GitInitAction;
        localization.Observe(this, () => Title = localization.Text(action));
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var source = clone ? AddField(fields, localization, "git.setup.source", WorkbenchText.GitSetupSource) : null;
        var destination = AddField(fields, localization, "git.setup.destination", WorkbenchText.GitSetupDestination);
        var browse = CreateButton(localization, "git.setup.browse", WorkbenchText.GitSetupBrowse);
        var browseError = new TextBlock { IsVisible = false };
        localization.Observe(browseError, () => browseError.Text = localization.Text(WorkbenchText.GitSetupBrowseError));
        browse.Click += async (_, _) =>
        {
            browseError.IsVisible = false;
            try
            {
                if (!StorageProvider.CanPickFolder)
                    return;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()).ConfigureAwait(true);
                foreach (var folder in folders)
                {
                    using (folder)
                        destination.Text = folder.TryGetLocalPath() ?? destination.Text;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(logger, error, "Git destination folder picker", LogLevel.Warning);
                browseError.IsVisible = true;
            }
        };
        fields.Children.Add(browse);
        fields.Children.Add(browseError);

        var submit = CreateButton(localization, "git.setup.submit", action);
        submit.IsDefault = true;
        submit.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(destination.Text) && (!clone || !string.IsNullOrWhiteSpace(source?.Text)))
                Close(new Choice(destination.Text, source?.Text));
        };
        var cancel = CreateButton(localization, "git.setup.cancel", WorkbenchText.GitSetupCancel);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { submit, cancel },
        });
        Content = fields;
        Opened += (_, _) => (source ?? destination).Focus();
    }

    private static TextBox AddField(Panel fields, WorkbenchLocalization localization, string id, string slug)
    {
        var caption = new TextBlock();
        var input = new TextBox();
        localization.Observe(caption, () =>
        {
            var label = localization.Text(slug);
            caption.Text = label;
            AutomationProperties.SetName(input, label);
        });
        AutomationProperties.SetAutomationId(input, id);
        fields.Children.Add(caption);
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
