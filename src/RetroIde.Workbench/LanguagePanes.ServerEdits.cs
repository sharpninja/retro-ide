using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using RetroIde.Application.LanguageServers;

using static RetroIde.Workbench.DockPaneControls;

namespace RetroIde.Workbench;

internal static partial class LanguagePanes
{
    private static Control ServerEditReviews(Control scope, LanguageServersViewModel view)
    {
        var locale = view.Localization;
        var selector = new ListBox
        {
            MaxHeight = 125,
            ItemTemplate = RowTemplate<LanguageServerEditPreview>(edit => (edit.Label, edit.Server.Name ?? edit.Server.ProfileId)),
        };
        SetLabel(selector, locale, WorkbenchText.LanguageServerEditReviews, "language.server-edits.items");
        var preview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        SetLabel(preview, locale, WorkbenchText.LanguageServerEditPreview, "language.server-edits.preview");
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                selector,
                new ScrollViewer { MaxHeight = 190, Content = preview },
                new WrapPanel
                {
                    Children =
                    {
                        Action(scope, WorkbenchCommands.LanguageApplyServerEdit, view.ApplyServerEditCommand, locale),
                        Action(scope, WorkbenchCommands.LanguageDeclineServerEdit, view.DeclineServerEditCommand, locale),
                    },
                },
            },
        };
        var section = new Expander { Content = body, IsExpanded = true };
        locale.Observe(section, () => section.Header = locale.Text(WorkbenchText.LanguageServerEditReviews));
        ConnectSelection(section, selector, view, () => view.ServerEdits, () => view.SelectedServerEdit, edit => view.SelectedServerEdit = edit);
        Watch(section, view, () => section.IsVisible = view.ServerEdits.Count != 0);
        Watch(preview, view, () => preview.Text = view.ServerEditText);
        return section;
    }

    internal static Control CreateServerEditIndicator(LanguageServersViewModel view)
    {
        var locale = view.Localization;
        var button = new Button { Command = view.OpenPaneCommand };
        const string id = "language.server-edits.indicator";
        AutomationProperties.SetAutomationId(button, id);
        locale.Observe(button, () =>
        {
            var presentation = CommandPresentation.Create(WorkbenchCommands.OpenLanguageServers, locale);
            var label = locale.Text(WorkbenchText.LanguageServerEditIndicator);
            button.Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 5,
                Children = { presentation.CreateIcon(), new TextBlock { Text = label } },
            };
            AutomationProperties.SetName(button, label);
        });
        Watch(button, view, () => button.IsVisible = view.ServerEdits.Count > 0);
        return button;
    }
}
