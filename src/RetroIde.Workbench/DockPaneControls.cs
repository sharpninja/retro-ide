using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;

namespace RetroIde.Workbench;

internal static class DockPaneControls
{
    internal static Control Field(WorkbenchLocalization locale, string slug, string id, string? initial,
        Action<string?> onChange, bool multiline = false)
    {
        var input = new TextBox { Text = initial, AcceptsReturn = multiline, MaxHeight = multiline ? 88 : double.PositiveInfinity };
        SetLabel(input, locale, slug, id);
        input.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                onChange(input.Text);
        };
        return Labelled(input, locale, slug);
    }

    internal static Control Labelled(Control input, WorkbenchLocalization locale, string slug)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        locale.Observe(label, () => label.Text = locale.Text(slug));
        return new StackPanel { Spacing = 3, Children = { label, input } };
    }

    internal static void SetLabel(Control input, WorkbenchLocalization locale, string slug, string id)
    {
        AutomationProperties.SetAutomationId(input, id);
        locale.Observe(input, () => AutomationProperties.SetName(input, locale.Text(slug)));
    }

    internal static Button Action(Control scope, CommandDescriptor command, ICommand handler, WorkbenchLocalization locale)
    {
        scope.KeyBindings.Add(new KeyBinding { Command = handler, Gesture = KeyGesture.Parse(command.EffectiveGesture) });
        return CommandPresentation.CreateButton(command, handler, locale);
    }

    internal static FuncDataTemplate<TRow> RowTemplate<TRow>(Func<TRow, (string Title, string Detail)> describe)
        where TRow : class => new((row, _) =>
    {
        if (row is null)
            return new TextBlock();
        var (title, detail) = describe(row);
        return new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 },
            },
        };
    });

    internal static void ConnectSelection<T>(Control anchor, SelectingItemsControl control, INotifyPropertyChanged view,
        Func<IReadOnlyList<T>> items, Func<T?> selection, Action<T?> select) where T : class
    {
        var updating = false;
        control.SelectionChanged += (_, _) =>
        {
            if (!updating)
                select(control.SelectedItem as T);
        };
        Watch(anchor, view, () =>
        {
            updating = true;
            try
            {
                var current = items();
                if (!ReferenceEquals(control.ItemsSource, current))
                    control.ItemsSource = current;
                control.SelectedItem = selection();
            }
            finally { updating = false; }
        });
    }

    internal static void Watch(Control anchor, INotifyPropertyChanged view, Action update)
    {
        anchor.AttachedToVisualTree += (_, _) =>
        {
            view.PropertyChanged += OnChanged;
            update();
        };
        anchor.DetachedFromVisualTree += (_, _) => view.PropertyChanged -= OnChanged;
        void OnChanged(object? sender, PropertyChangedEventArgs args) => update();
    }
}
