using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using RetroIde.Application.Git;

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
        var applyDescriptor = view.IsStaged ? WorkbenchCommands.UnstageGitHunks : WorkbenchCommands.StageGitHunks;
        var apply = CommandPresentation.CreateButton(applyDescriptor, view.ApplyHunksCommand, view.Localization);
        apply.Bind(Visual.IsVisibleProperty, new Binding(nameof(view.HasHunks)) { Source = view });
        var refresh = CommandPresentation.CreateButton(WorkbenchCommands.RefreshGitDiff, view.RefreshCommand, view.Localization);
        var actions = new WrapPanel { Children = { refresh, apply } };
        var editor = new TextEditor { Document = view.Buffer, IsReadOnly = true, WordWrap = false };
        AutomationProperties.SetAutomationId(editor, "git.diff.patch");
        var hunks = CreateHunkList(view, editor);
        var header = new StackPanel { Margin = new Thickness(8), Spacing = 3, Children = { title, worktree, actions, hunks, result } };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        var content = new DockPanel { Children = { header, editor } };
        content.KeyBindings.AddRange([
            new KeyBinding { Command = view.CloseCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.Close.EffectiveGesture) },
            new KeyBinding { Command = view.RefreshCommand, Gesture = KeyGesture.Parse(WorkbenchCommands.RefreshGitDiff.EffectiveGesture) },
            new KeyBinding { Command = view.ApplyHunksCommand, Gesture = KeyGesture.Parse(applyDescriptor.EffectiveGesture) },
        ]);
        return content;
    }

    private static ListBox CreateHunkList(GitDiffViewModel view, TextEditor editor)
    {
        var hunks = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            MaxHeight = 116,
            ItemTemplate = new FuncDataTemplate<GitHunkPatch.Hunk>((hunk, _) => new TextBlock { Text = hunk?.Header }),
        };
        AutomationProperties.SetAutomationId(hunks, "git.diff.hunks");
        view.Localization.Observe(hunks, () => AutomationProperties.SetName(hunks, view.Localization.Text(WorkbenchText.GitDiffHunks)));
        hunks.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(view.Hunks)) { Source = view });
        hunks.Bind(Visual.IsVisibleProperty, new Binding(nameof(view.HasHunks)) { Source = view });
        hunks.SelectionChanged += (_, args) =>
        {
            view.SetSelectedHunks(hunks.SelectedItems?.OfType<GitHunkPatch.Hunk>() ?? []);
            if (args.AddedItems.Count > 0 && args.AddedItems[0] is GitHunkPatch.Hunk current)
                editor.ScrollToLine(current.DiffLine);
        };
        return hunks;
    }
}
