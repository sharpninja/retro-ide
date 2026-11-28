using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static RetroIde.Workbench.DockPaneControls;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;

namespace RetroIde.Workbench;

internal static class EmulatorsPane
{
    private const string Buckets = "emulator.pane.buckets";
    private const string BucketId = "emulator.pane.bucket-id";
    private const string Repository = "emulator.pane.repository";
    private const string Configuration = "emulator.pane.configuration";
    private const string Releases = "emulator.pane.releases";
    private const string Installed = "emulator.pane.installed";
    private const string Project = "emulator.pane.project";
    private const string Previous = "emulator.pane.previous";
    private const string Active = "emulator.pane.active";
    private const string BindingReady = "emulator.pane.binding-ready";
    private const string BindingUnavailable = "emulator.pane.binding-unavailable";
    private const string BindingIncompatible = "emulator.pane.binding-incompatible";
    private const string Incompatible = "emulator.pane.incompatible";

    internal static readonly string[] TextSlugs =
        [Buckets, BucketId, Repository, Configuration, Releases, Installed, Project, Previous, Active, Incompatible,
         BindingReady, BindingUnavailable, BindingIncompatible,
         "emulator.pane.result-invalid"];

    internal static Control Create(EmulatorsViewModel view)
    {
        var locale = view.Localization;
        var pane = new StackPanel { Margin = new Thickness(8), Spacing = 8 };
        pane.Children.Add(Picker(pane, view, Buckets, "emulator.buckets.items",
            () => view.Buckets, () => view.SelectedBucket, value => view.SelectedBucket = value, value => (value.Id, value.Repository)));
        pane.Children.Add(new WrapPanel
        {
            Children =
            {
                Action(pane, WorkbenchCommands.ReloadEmulators, view.ReloadCommand, locale),
                Action(pane, WorkbenchCommands.RefreshEmulatorBucket, view.RefreshBucketCommand, locale),
                Action(pane, WorkbenchCommands.RemoveEmulatorBucket, view.RemoveBucketCommand, locale),
            },
        });
        var configuration = new Expander
        {
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Field(locale, BucketId, "emulator.bucket.id", view.BucketId, value => view.BucketId = value),
                    Field(locale, Repository, "emulator.bucket.repository", view.Repository, value => view.Repository = value),
                    Action(pane, WorkbenchCommands.ConfigureEmulatorBucket, view.ConfigureCommand, locale),
                },
            },
        };
        locale.Observe(configuration, () => configuration.Header = locale.Text(Configuration));
        pane.Children.Add(configuration);
        AddPackageControls(pane, view);
        AddProjectControls(pane, view);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Watch(pane, view, () => status.Text = view.LastResult?.Summary);
        SetLabel(status, locale, WorkbenchText.PaneTitle(EmulatorsViewModel.PaneId), "emulator.operation.status");
        pane.Children.Add(status);
        return new ScrollViewer { Content = pane };
    }

    private static void AddPackageControls(StackPanel pane, EmulatorsViewModel view)
    {
        var locale = view.Localization;
        pane.Children.Add(Picker(pane, view, Releases, "emulator.releases.items",
            () => view.SelectedBucket?.Releases ?? [], () => view.SelectedRelease, value => view.SelectedRelease = value,
            value => (value.Name, Detail(value.Pin, value.IsCompatible, locale))));
        pane.Children.Add(Action(pane, WorkbenchCommands.InstallEmulatorBundle, view.InstallCommand, locale));
        pane.Children.Add(Picker(pane, view, Installed, "emulator.installed.items",
            () => view.Installations, () => view.SelectedInstallation, value => view.SelectedInstallation = value,
            value => (value.Name, Detail(value.Pin, value.IsCompatible, locale))));
        pane.Children.Add(new WrapPanel
        {
            Children =
            {
                Action(pane, WorkbenchCommands.UpdateEmulatorBundle, view.UpdateCommand, locale),
                Action(pane, WorkbenchCommands.RemoveEmulatorBundle, view.RemoveCommand, locale),
            },
        });
    }

    private static void AddProjectControls(StackPanel pane, EmulatorsViewModel view)
    {
        var locale = view.Localization;
        pane.Children.Add(Picker(pane, view, Project, "emulator.projects.items",
            () => view.Projects, () => view.SelectedProject, value => view.SelectedProject = value,
            value => ProjectLabel(value.Path)));
        var active = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetAutomationId(active, "emulator.project.active");
        Watch(pane, view, () =>
        {
            active.IsVisible = view.ActivePin is not null;
            active.Text = view.ActivePin is { } pin ? DisplayPin(pin) : null;
        });
        var activeSection = Labelled(active, locale, Active);
        Watch(pane, view, () => activeSection.IsVisible = view.ActivePin is not null);
        pane.Children.Add(activeSection);
        var binding = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetAutomationId(binding, "emulator.project.binding");
        void ShowBinding()
        {
            binding.IsVisible = view.ActiveBinding is not null;
            binding.Text = view.ActiveBinding switch
            {
                { Status: "ready" } value =>
                    $"{locale.Text(BindingReady)} · {value.AdapterId} · {value.RuntimeIdentifier} · {value.ContractVersion}",
                { Status: "incompatible" } => locale.Text(BindingIncompatible),
                not null => locale.Text(BindingUnavailable),
                _ => null,
            };
        }
        Watch(pane, view, ShowBinding);
        locale.Observe(binding, ShowBinding);
        pane.Children.Add(binding);
        pane.Children.Add(Picker(pane, view, Previous, "emulator.previous.items",
            () => view.PreviousPins, () => view.SelectedPrevious, value => view.SelectedPrevious = value,
            value => (DisplayPin(value), value.ManifestRevision)));
        pane.Children.Add(new WrapPanel
        {
            Children =
            {
                Action(pane, WorkbenchCommands.SelectEmulatorBundle, view.SelectCommand, locale),
                Action(pane, WorkbenchCommands.RollbackEmulatorBundle, view.RollbackCommand, locale),
            },
        });
    }

    private static Control Picker<T>(Control anchor, EmulatorsViewModel view, string slug, string automationId,
        Func<IReadOnlyList<T>> items, Func<T?> selected, Action<T?> select, Func<T, (string Title, string Detail)> inspect) where T : class
    {
        var picker = new ComboBox { ItemTemplate = RowTemplate(inspect) };
        SetLabel(picker, view.Localization, slug, automationId);
        ConnectSelection(anchor, picker, view, items, selected, select);
        return Labelled(picker, view.Localization, slug);
    }

    private static string DisplayPin(Pin pin) => $"{pin.Bucket}/{pin.Bundle} {pin.Version}";

    private static (string Title, string Detail) ProjectLabel(string path)
    {
        var title = Path.GetFileName(path);
        var displayPath = OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
        return title.Length == 0 ? (displayPath, string.Empty) : (title, displayPath);
    }

    private static string Detail(Pin pin, bool compatible, WorkbenchLocalization locale) =>
        compatible ? DisplayPin(pin) : $"{DisplayPin(pin)} · {locale.Text(Incompatible)}";
}
