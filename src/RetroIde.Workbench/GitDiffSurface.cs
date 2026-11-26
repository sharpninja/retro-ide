using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;

namespace RetroIde.Workbench;

internal static class GitDiffSurface
{
    internal static Control Create(GitDiffViewModel view)
    {
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        view.Localization.Observe(title, () => title.Text = view.Describe(view.Identity.Path));
        var worktree = new TextBlock { Text = view.Identity.Worktree, TextWrapping = TextWrapping.Wrap };
        var result = new TextBlock();
        result.Bind(TextBlock.TextProperty, new Binding(nameof(view.LastResult) + ".Summary") { Source = view });
        var header = new StackPanel { Margin = new Thickness(8), Spacing = 3, Children = { title, worktree, result } };
        var editor = new TextEditor { Document = view.Buffer, IsReadOnly = true, WordWrap = false };
        AutomationProperties.SetAutomationId(editor, "git.diff.patch");
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        var content = new DockPanel { Children = { header, editor } };
        content.KeyBindings.Add(new KeyBinding { Command = view.CloseCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.Close.EffectiveGesture) });
        return content;
    }
}
