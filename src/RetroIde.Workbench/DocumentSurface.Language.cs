using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace RetroIde.Workbench;

internal static partial class DocumentSurface
{
    private static Control CreateLanguageBar(Control surface, DocumentViewModel view)
    {
        var catalog = view.Localization;
        var rename = new TextBox { MinWidth = 175 };
        catalog.Observe(rename, () =>
        {
            rename.PlaceholderText = catalog.Text(WorkbenchText.LanguageNewSymbol);
            AutomationProperties.SetName(rename, rename.PlaceholderText);
        });
        AutomationProperties.SetAutomationId(rename, "document.language.rename");
        rename.Text = view.LanguageRenameText;
        rename.TextChanged += (_, _) => view.LanguageRenameText = rename.Text;

        var more = new Expander
        {
            Padding = new Thickness(6, 2),
            Content = new WrapPanel
            {
                Children =
                {
                    CreateAction(surface, WorkbenchCommands.LanguageSignature, view.LanguageSignatureHelpCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageDefinition, view.LanguageDefinitionCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageDeclaration, view.LanguageDeclarationCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageType, view.LanguageTypeDefinitionCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageImplementation, view.LanguageImplementationCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageReferences, view.LanguageReferencesCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageSymbols, view.LanguageSymbolsCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageDiagnostics, view.LanguageDiagnosticsCommand, catalog),
                    CreateAction(surface, WorkbenchCommands.LanguageActions, view.LanguageCodeActionsCommand, catalog),
                    rename, CreateAction(surface, WorkbenchCommands.LanguageRename, view.LanguageRenameCommand, catalog),
                },
            },
        };
        catalog.Observe(more, () => more.Header = catalog.Text(WorkbenchText.LanguageTools));
        AutomationProperties.SetAutomationId(more, "document.language.tools");
        return new StackPanel
        {
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        CreateAction(surface, WorkbenchCommands.LanguageComplete, view.LanguageCompleteCommand, catalog),
                        CreateAction(surface, WorkbenchCommands.LanguageHover, view.LanguageHoverCommand, catalog),
                        CreateAction(surface, WorkbenchCommands.LanguageFormat, view.LanguageFormatCommand, catalog),
                    },
                },
                more,
            },
        };
    }

    private static Control CreateLanguageResult(Control surface, DocumentViewModel view)
    {
        var catalog = view.Localization;
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(message, "document.language.message");
        var messageScroll = new ScrollViewer { MaxHeight = 180, Content = message };
        var preview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        var previewScroll = new ScrollViewer { MaxHeight = 180, Content = preview };
        var results = new ListBox
        {
            MaxHeight = 180,
            ItemTemplate = new FuncDataTemplate<LanguageEditorRow>((row, _) => new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = row?.Label, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = row?.Detail, IsVisible = !string.IsNullOrWhiteSpace(row?.Detail), TextWrapping = TextWrapping.Wrap },
                },
            }),
        };
        AutomationProperties.SetAutomationId(preview, "document.language.preview");
        AutomationProperties.SetAutomationId(results, "document.language.results");
        results.SelectionChanged += (_, _) => view.SelectedLanguageRow = results.SelectedItem as LanguageEditorRow;
        var open = CreateAction(surface, WorkbenchCommands.LanguageOpenResult, view.OpenLanguageResultCommand, catalog);
        var apply = CreateAction(surface, WorkbenchCommands.LanguageApply, view.ApplyLanguageEditCommand, catalog);
        var section = new StackPanel
        {
            Margin = new Thickness(0, 4),
            Children = { messageScroll, previewScroll, results, new WrapPanel { Children = { open, apply } } },
        };

        void Update()
        {
            var result = view.LanguageResult;
            section.IsVisible = result is not null;
            var text = result?.Message;
            messageScroll.IsVisible = result is { EditPreviewId: null, Rows.Count: 0 };
            message.Text = string.IsNullOrWhiteSpace(text) ? catalog.Text(WorkbenchText.LanguageNoResults) : text;
            previewScroll.IsVisible = result?.EditPreviewId is not null;
            preview.Text = text;
            results.IsVisible = result?.Rows.Count > 0;
            if (!ReferenceEquals(results.ItemsSource, result?.Rows))
                results.ItemsSource = result?.Rows;
            if (!ReferenceEquals(results.SelectedItem, view.SelectedLanguageRow))
                results.SelectedItem = view.SelectedLanguageRow;
            open.IsVisible = result?.Rows.Any(row => row.Request is not null) == true;
            apply.IsVisible = result?.EditPreviewId is not null;
        }

        void OnLanguageResultChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(DocumentViewModel.LanguageResult) or nameof(DocumentViewModel.SelectedLanguageRow))
                Update();
        }

        section.AttachedToVisualTree += (_, _) =>
        {
            view.PropertyChanged += OnLanguageResultChanged;
            Update();
        };
        section.DetachedFromVisualTree += (_, _) => view.PropertyChanged -= OnLanguageResultChanged;
        catalog.Observe(section, Update);
        return section;
    }
}
