using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RetroIde.Workbench;

internal sealed class GitPatchDialog : Window
{
    internal GitPatchDialog(SourceControlViewModel view)
    {
        Width = 680;
        Height = 480;
        MinWidth = 460;
        MinHeight = 370;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        view.Localization.Observe(this, () => Title = view.Localization.Text(WorkbenchText.GitPatchTitle));
        var worktree = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        worktree.Bind(TextBlock.TextProperty, Bind(nameof(view.PatchWorktree)));
        var layer = new ComboBox
        {
            ItemsSource = new[] { Layer("index", WorkbenchText.GitPatchIndex), Layer("worktree", WorkbenchText.GitPatchWorktree) },
            SelectedValueBinding = new Binding(nameof(ComboBoxItem.Tag)),
        };
        AutomationProperties.SetAutomationId(layer, "git.patch.layer");
        view.Localization.Observe(layer, () => AutomationProperties.SetName(layer, view.Localization.Text(WorkbenchText.GitPatchLayer)));
        layer.Bind(ComboBox.SelectedValueProperty, Bind(nameof(view.PatchLayer), BindingMode.TwoWay));
        var file = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        file.Bind(TextBlock.TextProperty, Bind(nameof(view.PatchFileName)));
        var paths = new ListBox();
        AutomationProperties.SetAutomationId(paths, "git.patch.paths");
        view.Localization.Observe(paths, () => AutomationProperties.SetName(paths, view.Localization.Text(WorkbenchText.GitPatchPaths)));
        paths.Bind(ItemsControl.ItemsSourceProperty, Bind($"{nameof(view.PatchReview)}.Paths"));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "git.patch.status");
        status.Bind(TextBlock.TextProperty, Bind(nameof(view.PatchStatus)));
        var close = new Button { IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(close, "git.patch.close");
        view.Localization.Observe(close, () => close.Content = view.Localization.Text(WorkbenchText.GitRemoteClose));
        close.Click += (_, _) => Close();
        var actions = new WrapPanel
        {
            Children = { layer, Action(WorkbenchCommands.ExportGitPatch, view.ExportPatchCommand),
                Action(WorkbenchCommands.ReviewGitPatch, view.ReviewPatchCommand), Action(WorkbenchCommands.ApplyGitPatch, view.ApplyPatchCommand) },
        };
        var grid = new Grid { Margin = new Thickness(14), RowSpacing = 8, RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
        Control[] rows = [worktree, actions, file, paths, status, close];
        for (var row = 0; row < rows.Length; row++)
        {
            Grid.SetRow(rows[row], row);
            grid.Children.Add(rows[row]);
        }
        Content = view.WrapPatches(grid);

        Binding Bind(string path, BindingMode mode = BindingMode.OneWay) => new(path) { Source = view, Mode = mode };
        ComboBoxItem Layer(string id, string label)
        {
            var item = new ComboBoxItem { Tag = id };
            view.Localization.Observe(item, () => item.Content = view.Localization.Text(label));
            return item;
        }
        Button Action(CommandDescriptor descriptor, ICommand command)
        {
            WorkbenchWindow.RegisterShortcut(this, command, Avalonia.Input.KeyGesture.Parse(descriptor.EffectiveGesture));
            return CommandPresentation.CreateButton(descriptor, command, view.Localization);
        }
    }

    internal async Task<SourceControlViewModel.PatchFile?> PickAsync(CancellationToken token)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }).ConfigureAwait(true);
        if (files.Count == 0)
            return null;
        using var file = files[0];
        var input = await file.OpenReadAsync().ConfigureAwait(true);
        using var bytes = new MemoryStream();
        await using (input.ConfigureAwait(true))
            await input.CopyToAsync(bytes, token).ConfigureAwait(true);
        return new(file.Name, bytes.ToArray());
    }

    internal async Task<bool> SaveAsync(byte[] bytes, CancellationToken token)
    {
        using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "changes.patch", DefaultExtension = "patch", ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        if (file is null)
            return false;
        token.ThrowIfCancellationRequested();
        var output = await file.OpenWriteAsync().ConfigureAwait(true);
        await using (output.ConfigureAwait(true))
        {
            if (output.CanSeek)
                output.SetLength(0);
            await output.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(true);
        }
        return true;
    }
}
