using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02LiveLanguageContractTests
{
    /// <summary>Changes the language through rendered menus and invokes the real keyboard binding after switching.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-008#ac-test-live-language. Prove open-window refresh and compare a fresh workbench using the same preference.</remarks>
    /// <remarks>2026-11-26 · TEST-RETRO-008#ac-test-live-language. Resolve the actual automation ID for the installed locale tag.</remarks>
    [AvaloniaFact]
    public async Task Workbench_refreshes_localized_presentation_and_reopens_in_the_selected_language()
    {
        using var files = new LanguageFiles();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Assert.True(typeof(WorkbenchViewModel).GetProperty("SetDisplayLanguageCommand", Access) is not null,
            "The live workbench does not expose a display-language command.");
        await using var driver = new LiveLanguage(files, timeout.Token);
        await VerifyLanguageAsync(driver);
    }

    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class LiveLanguage(LanguageFiles files, CancellationToken token) : ILanguage, IAsyncDisposable
    {
        private WorkbenchSession? _session;
        private WorkbenchWindow? _window;
        private Window? _floating;
        private Button? _folder;

        public async Task<LanguageState> OpenAsync()
        {
            var localizationType = Assert.IsAssignableFrom<Type>(typeof(WorkbenchSession).Assembly.GetType("RetroIde.Workbench.WorkbenchLocalization"));
            var localization = Activator.CreateInstance(localizationType, Access, binder: null, [files.Home, CultureInfo.GetCultureInfo("en")], culture: null);
            var storeType = Assert.IsAssignableFrom<Type>(Type.GetType("RetroIde.Application.Workspaces.WorkspaceExplorerStore, RetroIde.Application"));
            var store = Activator.CreateInstance(storeType, Access, binder: null, [Path.Combine(files.Home, "workspace.json")], culture: null);
            _session = Assert.IsType<WorkbenchSession>(Activator.CreateInstance(typeof(WorkbenchSession), Access,
                binder: null, [store, null, localization], culture: null));
            _window = new WorkbenchWindow(_session) { Width = 1440, Height = 950 };
            _window.Show();
            _session.Layout.Factory.FloatDockable(_session.Layout.Tool("explorer"));
            _floating = Assert.IsAssignableFrom<Window>(Assert.Single(_session.Layout.Root.Windows!).Host);
            while (_folder is null)
            {
                _floating.UpdateLayout();
                _folder = _floating.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                    string.Equals(AutomationProperties.GetAutomationId(button), "workspace.open-folder.toolbar", StringComparison.Ordinal));
                if (_folder is null)
                    await Task.Delay(20, token);
            }
            return ReadWindow();
        }

        public async Task<LanguageState> ChangeAsync(string locale)
        {
            var session = Assert.IsType<WorkbenchSession>(_session);
            var window = Assert.IsType<WorkbenchWindow>(_window);
            var view = window.GetVisualDescendants().OfType<MenuItem>().Single(menu => Equals(menu.Header, "_View"));
            view.IsSubMenuOpen = true;
            window.UpdateLayout();
            var menu = window.GetVisualDescendants().OfType<MenuItem>().Single(item => Equals(item.Header, "Display Language"));
            menu.IsSubMenuOpen = true;
            var entry = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.CommandParameter is string tag &&
                string.Equals(tag, locale, StringComparison.OrdinalIgnoreCase));
            var automationId = Assert.IsType<string>(AutomationProperties.GetAutomationId(entry));
            await WorkbenchRemoteControl.ClickAsync(window, automationId);
            var set = Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(session.View, "SetDisplayLanguageCommand"));
            await Assert.IsAssignableFrom<Task>(set.ExecutionTask).WaitAsync(token);
            Assert.Equal("ok", session.View.LastResult?.Code);
            window.UpdateLayout();
            return ReadWindow();
        }

        public async Task<int> InvokeShortcutAsync()
        {
            var session = Assert.IsType<WorkbenchSession>(_session);
            var window = Assert.IsType<WorkbenchWindow>(_window);
            Assert.True(session.Layout.Focus("output"));
            session.View.FocusTarget = "registers";
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(window.FocusButton.Command);
            var previous = command.ExecutionTask;
            var count = Assert.IsType<int>(Read(session.Controller, "DispatchCount"));
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            var dispatched = Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.NotSame(previous, dispatched);
            await dispatched.WaitAsync(token);
            Assert.True(session.Layout.IsFocused("registers"));
            return Assert.IsType<int>(Read(session.Controller, "DispatchCount")) - count;
        }

        public async Task<LanguageState> ReopenAsync()
        {
            await CloseAsync();
            return await OpenAsync();
        }

        private LanguageState ReadWindow()
        {
            var session = Assert.IsType<WorkbenchSession>(_session);
            var window = Assert.IsType<WorkbenchWindow>(_window);
            var floating = Assert.IsAssignableFrom<Window>(_floating);
            var folder = Assert.IsAssignableFrom<Button>(_folder);
            var localization = Assert.IsAssignableFrom<object>(Read(session, "Localization"));
            var culture = Assert.IsAssignableFrom<CultureInfo>(Read(localization, "DisplayCulture"));
            var toolbar = window.FocusButton;
            var label = window.FocusButtonLabel;
            var menuIcon = window.FocusMenu.Icon is Image { Source: Bitmap { PixelSize.Width: > 0 } };
            var barIcon = toolbar.GetVisualDescendants().OfType<Image>().Any(image => image.IsVisible && image.Source is Bitmap { PixelSize.Width: > 0 });
            Assert.Equal(menuIcon, barIcon);
            window.UpdateLayout();
            var topLeft = Assert.IsType<Point>(label.TranslatePoint(new Point(), toolbar));
            var bottomRight = Assert.IsType<Point>(label.TranslatePoint(new Point(label.Bounds.Width, label.Bounds.Height), toolbar));
            var fits = label.IsEffectivelyVisible && label.Bounds.Width >= label.DesiredSize.Width &&
                topLeft.X >= -1 && topLeft.Y >= -1 && bottomRight.X <= toolbar.Bounds.Width + 1 && bottomRight.Y <= toolbar.Bounds.Height + 1;
            var shortcut = window.FocusShortcut;
            var command = Assert.IsAssignableFrom<object>(toolbar.Command);
            Assert.Same(command, shortcut.Command);
            Assert.Same(command, window.FocusMenu.Command);
            Assert.Equal(shortcut.Gesture, window.FocusMenu.InputGesture);
            return new LanguageState(culture.Name, label.Text!, AutomationProperties.GetName(toolbar)!, AutomationProperties.GetName(window.FocusMenu)!,
                AutomationProperties.GetName(folder)!, session.Layout.Tool("explorer").Title!, window.FlowDirection == FlowDirection.RightToLeft,
                floating.FlowDirection == FlowDirection.RightToLeft, menuIcon, label.Bounds.Width, fits, command, shortcut.Gesture!.ToString());
        }

        private async Task CloseAsync()
        {
            try
            {
                if (_window is not { } window)
                    return;
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                window.Close();
                await closed.Task.WaitAsync(token);
            }
            finally
            {
                _session?.Dispose();
                _session = null;
                _window = null;
                _floating = null;
                _folder = null;
            }
        }

        public async ValueTask DisposeAsync() => await CloseAsync();

        private static object? Read(object source, string property) =>
            Assert.IsAssignableFrom<PropertyInfo>(source.GetType().GetProperty(property, Access)).GetValue(source);
    }
}
