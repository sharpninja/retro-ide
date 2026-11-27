using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace RetroIde.Workbench;

internal static partial class DocumentSurface
{
    private static readonly RegistryOptions Grammars = new(ThemeName.DarkPlus);

    public static Control Create(DocumentViewModel view)
    {
        var surface = new DockPanel { Margin = new Thickness(6) };
        var editor = new TextEditor { Document = view.Buffer, ShowLineNumbers = true };
        AutomationProperties.SetAutomationId(editor, "document.source-editor");
        var status = new TextBlock();
        status.Bind(TextBlock.TextProperty, new Binding(nameof(DocumentViewModel.LastResult) + ".Summary") { Source = view });

        var discard = CreateAction(surface, WorkbenchCommands.Discard, view.DiscardAndCloseCommand, view.Localization);
        discard.Bind(Visual.IsVisibleProperty, new Binding(nameof(DocumentViewModel.CanDiscard)) { Source = view });
        var tools = new WrapPanel
        {
            Children =
            {
                CreateAction(surface, WorkbenchCommands.Save, view.SaveCommand, view.Localization), CreateAction(surface, WorkbenchCommands.Reveal, view.RevealCommand, view.Localization),
                CreateAction(surface, WorkbenchCommands.Search, view.ShowSearchCommand, view.Localization),
                CreateAction(surface, WorkbenchCommands.Undo, view.UndoCommand, view.Localization), CreateAction(surface, WorkbenchCommands.Redo, view.RedoCommand, view.Localization),
                CreateAction(surface, WorkbenchCommands.Close, view.CloseCommand, view.Localization), discard,
            },
        };
        var panel = CreateSearchPanel(surface, editor, view);
        var language = CreateLanguageBar(surface, view);
        var languageResult = CreateLanguageResult(surface, view);
        DockPanel.SetDock(language, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(languageResult, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(tools, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(panel, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        surface.Children.AddRange([tools, panel, language, status, languageResult, editor]);
        BindEditor(editor, view);
        return surface;
    }

    private static Control CreateSearchPanel(Control surface, TextEditor editor, DocumentViewModel view)
    {
        var query = new TextBox { MinWidth = 150 };
        query.Bind(TextBox.TextProperty, new Binding(nameof(DocumentViewModel.SearchText)) { Source = view, Mode = BindingMode.TwoWay });
        AutomationProperties.SetAutomationId(query, "document.find.query");
        var replacement = new TextBox { MinWidth = 150 };
        replacement.Bind(TextBox.TextProperty, new Binding(nameof(DocumentViewModel.Replacement)) { Source = view, Mode = BindingMode.TwoWay });
        AutomationProperties.SetAutomationId(replacement, "document.find.replacement");
        var matchCase = new CheckBox();
        view.Localization.Observe(query, () =>
        {
            query.PlaceholderText = view.Localization.Text(WorkbenchText.SearchQuery);
            AutomationProperties.SetName(query, query.PlaceholderText);
        });
        view.Localization.Observe(replacement, () =>
        {
            replacement.PlaceholderText = view.Localization.Text(WorkbenchText.SearchReplacement);
            AutomationProperties.SetName(replacement, replacement.PlaceholderText);
        });
        view.Localization.Observe(matchCase, () => matchCase.Content = view.Localization.Text(WorkbenchText.SearchMatchCase));
        matchCase.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(DocumentViewModel.MatchCase)) { Source = view, Mode = BindingMode.TwoWay });

        var panel = new WrapPanel
        {
            IsVisible = false,
            Children =
            {
                query, replacement, matchCase, CreateAction(surface, WorkbenchCommands.Next, view.FindCommand, view.Localization),
                CreateAction(surface, WorkbenchCommands.Previous, view.FindPreviousCommand, view.Localization), CreateAction(surface, WorkbenchCommands.Replace, view.ReplaceCommand, view.Localization),
                CreateAction(surface, WorkbenchCommands.ReplaceAll, view.ReplaceAllCommand, view.Localization),
            },
        };
        AutomationProperties.SetAutomationId(panel, "document.find.panel");

        void ShowSearch(object? sender, EventArgs args)
        {
            panel.IsVisible = true;
            query.Focus();
            query.SelectAll();
        }

        panel.AttachedToVisualTree += (_, _) => view.SearchRequested += ShowSearch;
        panel.DetachedFromVisualTree += (_, _) => view.SearchRequested -= ShowSearch;
        panel.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            panel.IsVisible = false;
            editor.Focus();
            args.Handled = true;
        };
        query.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? view.FindPreviousCommand : view.FindCommand).Execute(null);
            args.Handled = true;
        };
        return panel;
    }

    private static void BindEditor(TextEditor editor, DocumentViewModel view)
    {
        TextMate.Installation? syntax = null;
        editor.AttachedToVisualTree += (_, _) =>
        {
            ApplyPosition(editor, view);
            editor.TextArea.Caret.PositionChanged += OnPositionChanged;
            editor.TextArea.SelectionChanged += OnPositionChanged;
            view.PositionRequested += OnPositionRequested;
            var extension = Path.GetExtension(view.Address.RelativePath);
            if (Grammars.GetLanguageByExtension(extension) is { } language)
            {
                syntax = editor.InstallTextMate(Grammars);
                syntax.SetGrammar(Grammars.GetScopeByLanguageId(language.Id));
            }
        };
        editor.DetachedFromVisualTree += (_, _) =>
        {
            editor.TextArea.Caret.PositionChanged -= OnPositionChanged;
            editor.TextArea.SelectionChanged -= OnPositionChanged;
            view.PositionRequested -= OnPositionRequested;
            syntax?.Dispose();
            syntax = null;
        };

        void OnPositionChanged(object? sender, EventArgs args) =>
            view.EditorPosition = (editor.CaretOffset, editor.SelectionStart, editor.SelectionLength);

        void OnPositionRequested(object? sender, EventArgs args) => ApplyPosition(editor, view);
    }

    private static void ApplyPosition(TextEditor editor, DocumentViewModel view)
    {
        var position = view.EditorPosition;
        var start = Math.Clamp(position.SelectionStart, 0, editor.Document.TextLength);
        editor.Select(start, Math.Clamp(position.SelectionLength, 0, editor.Document.TextLength - start));
        editor.CaretOffset = Math.Clamp(position.Caret, 0, editor.Document.TextLength);
        editor.ScrollToLine(editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
    }

    private static Button CreateAction(Control scope, CommandDescriptor descriptor, ICommand command, WorkbenchLocalization localization)
    {
        scope.KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
        return CommandPresentation.CreateButton(descriptor, command, localization);
    }
}
