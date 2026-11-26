using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.RemoteControl.Protocol.V1;
using Avalonia.RemoteControl.Server;
using Avalonia.RemoteControl.Server.Input;
using Avalonia.RemoteControl.Server.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Model.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlWorkflowTests
{
    /// <remarks>TEST-RETRO-008#ac-test-live-language; TEST-RETRO-011#ac-test-buffer-state; TEST-RETRO-011#ac-test-save-source; TEST-RETRO-012#ac-test-explorer-mixed-roots; TEST-RETRO-012#ac-test-explorer-persistence; TEST-RETRO-012#ac-test-explorer-changes</remarks>
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var assets = Directory.CreateTempSubdirectory("retro-ide-native-workflow-");
        try
        {
            var settings = new WorkspaceExplorerStore(Path.Combine(assets.FullName, "workspace.json"));
            WriteLanguagePack(assets);
            var state = await ExerciseWorkbenchAsync(assets, settings, timeout.Token).ConfigureAwait(true);
            await VerifyRestartAsync(assets, settings, state, timeout.Token).ConfigureAwait(true);
        }
        finally { assets.Delete(recursive: true); }
    }

    private static async Task<RestoredState> ExerciseWorkbenchAsync(DirectoryInfo assets, WorkspaceExplorerStore settings, CancellationToken token)
    {
        var folder = assets.CreateSubdirectory("folder").FullName;
        var project = assets.CreateSubdirectory("project").FullName;
        var path = Path.Combine(folder, "demo.cs");
        var otherPath = Path.Combine(project, "demo.cs");
        await File.WriteAllTextAsync(path, "class First {}", token).ConfigureAwait(true);
        await File.WriteAllTextAsync(otherPath, "class Second {}", token).ConfigureAwait(true);
        using var session = CreateSession(assets, settings);
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var first = await OpenAsync(window, session, folder, project: false, token).ConfigureAwait(true);
            var second = await OpenAsync(window, session, project, project: true, token).ConfigureAwait(true);
            Require(first.Address.RootId != second.Address.RootId &&
                string.Equals(first.Address.RelativePath, second.Address.RelativePath, StringComparison.Ordinal),
                "Same-name sources did not open in distinct workspace roots.");
            var secondEditor = await EditorAsync(window, second, token).ConfigureAwait(true);
            Require(!ReferenceEquals(first.Buffer, second.Buffer) && string.Equals(secondEditor.Text, "class Second {}", StringComparison.Ordinal),
                "The project tab shared a buffer or loaded the same-name file from the wrong root.");

            await VerifyEditorAsync(window, session, first, path, token).ConfigureAwait(true);
            Require(string.Equals(await File.ReadAllTextAsync(otherPath, token).ConfigureAwait(true), "class Second {}", StringComparison.Ordinal),
                "Saving the folder tab changed the project file.");
            await File.WriteAllTextAsync(Path.Combine(folder, "appeared.cs"), "// added outside the IDE", token).ConfigureAwait(true);
            await WaitAsync(() => session.ExplorerView.TreeRoots[0].Children.Any(node =>
                string.Equals(node.Label, "appeared.cs", StringComparison.Ordinal)), "external file refresh", token).ConfigureAwait(true);
            await VerifyLanguageAsync(window, session, token).ConfigureAwait(true);

            var before = session.Explorer.Snapshot();
            Require(before.Selection == first.Address, $"The live Explorer changed its selection after dock and locale changes ({before.Selection}).");
            Require(before.Expanded.Length == 2, $"The live Explorer lost expanded roots after dock and locale changes ({before.Expanded.Length}).");
            await CloseAsync(window).ConfigureAwait(true);
            return new RestoredState(session.View.WorkspaceId, session.View.ViewKey, first.Address);
        }
        finally { await CloseAsync(window).ConfigureAwait(true); }
    }

    private static WorkbenchSession CreateSession(DirectoryInfo assets, WorkspaceExplorerStore settings) =>
        new(settings, savedLayout: null, new WorkbenchLocalization(assets.FullName, CultureInfo.GetCultureInfo("en")));

    private static async Task<DocumentViewModel> OpenAsync(WorkbenchWindow window, WorkbenchSession session, string path,
        bool project, CancellationToken token)
    {
        Require(session.Layout.Focus("explorer"), "The native Project Explorer is missing.");
        var explorer = session.ExplorerView;
        var rootCount = explorer.TreeRoots.Count;
        explorer.PickDirectoryAsync = _ => Task.FromResult<string?>(path);
        await ClickAsync(window, project ? "workspace.open-project.toolbar" : "workspace.open-folder.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => explorer.TreeRoots.Count == rootCount + 1, "open workspace root", token).ConfigureAwait(true);
        var root = explorer.TreeRoots[^1];
        var rootItem = await FindAsync(window, (TreeViewItem item) => ReferenceEquals(item.DataContext, root), token).ConfigureAwait(true);
        await WaitAsync(() => rootItem.IsLoaded, "load the Explorer root row", token).ConfigureAwait(true);
        rootItem.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
        await WaitAsync(() => root.IsExpanded && root.Children is [{ Address: not null }], "expand root", token).ConfigureAwait(true);
        return await ActivateAsync(window, session, root.Children[0], token).ConfigureAwait(true);
    }

    private static async Task<DocumentViewModel> ActivateAsync(WorkbenchWindow window, WorkbenchSession session, ExplorerTreeNode node,
        CancellationToken token)
    {
        Require(session.Layout.Focus("explorer"), "The native Project Explorer cannot receive focus.");
        var row = await FindAsync(window, (TreeViewItem item) => ReferenceEquals(item.DataContext, node), token).ConfigureAwait(true);
        var tree = row.FindAncestorOfType<TreeView>() ?? throw new InvalidOperationException("The Explorer row has no tree.");
        tree.SetCurrentValue(TreeView.SelectedItemProperty, node);
        await WaitAsync(() => ReferenceEquals(session.ExplorerView.SelectedNode, node), "select source row", token).ConfigureAwait(true);
        Require(row.Focus(), "The native Explorer row could not receive application focus.");
        await SendAsync(window, [new RemoteInputEvent { Kind = RemoteInputKind.KeyDown, Key = "Enter" }]).ConfigureAwait(true);
        await WaitAsync(() => session.Layout.CurrentSource?.Address == node.Address, "open source with Enter", token).ConfigureAwait(true);
        return session.Layout.CurrentSource!;
    }

    private static async Task VerifyEditorAsync(WorkbenchWindow window, WorkbenchSession session, DocumentViewModel first, string path,
        CancellationToken token)
    {
        var same = await ActivateAsync(window, session, session.ExplorerView.TreeRoots[0].Children[0], token).ConfigureAwait(true);
        Require(ReferenceEquals(first, same), "Reopening a source replaced its registered editor tab.");
        var editor = await EditorAsync(window, first, token).ConfigureAwait(true);
        Require(editor.ShowLineNumbers && string.Equals(editor.Text, "class First {}", StringComparison.Ordinal),
            "The native source editor did not show the expected file and line numbers.");
        editor.CaretOffset = editor.Document.TextLength;
        Require(editor.TextArea.Focus(), "The source editor did not receive application focus.");
        await SendAsync(window, [new RemoteInputEvent { Kind = RemoteInputKind.Text, Text = " // edited" }]).ConfigureAwait(true);
        const string edited = "class First {} // edited";
        Require(first.IsDirty && string.Equals(editor.Text, edited, StringComparison.Ordinal),
            "Remote text did not edit the registered buffer.");
        await ClickAsync(window, "document.search.toolbar", token).ConfigureAwait(true);
        var search = await FindAsync(window, (TextBox control) => HasId(control, "document.find.query"), token).ConfigureAwait(true);
        Require(search.Focus(), "The find field did not receive application focus.");
        await SendAsync(window, [new RemoteInputEvent { Kind = RemoteInputKind.Text, Text = "First" }]).ConfigureAwait(true);
        await ClickAsync(window, "document.next.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(editor.SelectedText, "First", StringComparison.Ordinal), "find text", token).ConfigureAwait(true);

        await FloatAndRedockAsync(window, session, first, editor, token).ConfigureAwait(true);
        await ClickAsync(window, "document.save.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => !first.IsDirty, "save source buffer", token).ConfigureAwait(true);
        Require(string.Equals(await File.ReadAllTextAsync(path, token).ConfigureAwait(true), edited, StringComparison.Ordinal),
            "The editor toolbar did not save the intended source file.");
        await ClickAsync(window, "document.reveal.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => session.Layout.IsFocused("explorer") && session.ExplorerView.SelectedNode?.Address == first.Address,
            "reveal source in Explorer", token).ConfigureAwait(true);
        Console.WriteLine("Native RemoteControl opened same-name files from mixed roots, edited and found source text, preserved the editor across float and redock, saved, and revealed the exact root-relative source.");
    }

    private static async Task FloatAndRedockAsync(WorkbenchWindow window, WorkbenchSession session, DocumentViewModel source,
        TextEditor editor, CancellationToken token)
    {
        var factory = session.Layout.Factory;
        var dockable = factory.FindDockable(session.Layout.Root, candidate => string.Equals(candidate.Id, source.Scope, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Cannot find the editor tab in the Dock model.");
        var owner = (IDock)dockable.Owner!;
        var anchor = owner.VisibleDockables!.First(candidate => !ReferenceEquals(candidate, dockable));
        factory.FloatDockable(dockable);
        var floating = SeparateWindow(window, session);
        var detached = await EditorAsync(floating, source, token).ConfigureAwait(true);
        Require(ReferenceEquals(editor.Document, detached.Document) && string.Equals(detached.SelectedText, "First", StringComparison.Ordinal) &&
            detached.Document.UndoStack.CanUndo, "The floated editor lost its buffer, selection, or undo history.");

        factory.MoveDockable((IDock)dockable.Owner!, owner, dockable, anchor);
        factory.SetActiveDockable(dockable);
        var attached = await EditorAsync(window, source, token).ConfigureAwait(true);
        Require(ReferenceEquals(dockable.Owner, owner) && string.Equals(attached.SelectedText, "First", StringComparison.Ordinal) &&
            attached.Document.UndoStack.CanUndo, "The redocked editor lost selection or undo state.");
    }

    private static async Task VerifyLanguageAsync(WorkbenchWindow window, WorkbenchSession session, CancellationToken token)
    {
        session.Layout.Factory.FloatDockable(session.Layout.Tool("explorer"));
        var floating = SeparateWindow(window, session);
        var folder = await FindAsync(floating, (Button button) => HasId(button, "workspace.open-folder.toolbar"), token)
            .ConfigureAwait(true);
        window.Activate();
        var command = window.FocusButton.Command;
        var gesture = window.FocusShortcut.Gesture;
        var viewMenu = await FindAsync(window, (MenuItem item) => Equals(item.Header, "_View"), token).ConfigureAwait(true);
        viewMenu.Open();
        var languageMenu = viewMenu.ItemsSource!.OfType<MenuItem>().Single(item => Equals(item.Header, "Display Language"));
        await WaitAsync(() => TopLevel.GetTopLevel(languageMenu) is not null, "open View popup", token).ConfigureAwait(true);
        languageMenu.Open();
        var language = languageMenu.ItemsSource!.OfType<MenuItem>().Single(item => HasId(item, "workbench.language.ar"));
        await WaitAsync(() => TopLevel.GetTopLevel(language) is not null, "open Display Language popup", token).ConfigureAwait(true);
        Require(ReferenceEquals(language.Command, session.View.SetDisplayLanguageCommand) &&
            language.Command.CanExecute(language.CommandParameter), "The realized locale entry is missing its live command binding.");
        // Native menus render in their own popup roots. Send RemoteControl to that rendered input surface.
        await WorkbenchRemoteControl.ClickAsync(TopLevel.GetTopLevel(language)!, "workbench.language.ar").ConfigureAwait(true);
        var change = session.View.SetDisplayLanguageCommand.ExecutionTask ??
            throw new InvalidOperationException("The visible language command did not start.");
        await change.WaitAsync(token).ConfigureAwait(true);
        Require(string.Equals(session.Localization.DisplayCulture.Name, "ar", StringComparison.Ordinal),
            $"Selecting Arabic through the bound menu command returned {session.View.LastResult?.Code}: {session.View.LastResult?.Summary}.");
        viewMenu.Close();

        Require(window.FlowDirection == FlowDirection.RightToLeft && floating.FlowDirection == FlowDirection.RightToLeft &&
            window.FocusMenu.Icon is Image { Source: not null } &&
            string.Equals(window.FocusButtonLabel.Text, "تركيز النافذة", StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetName(window.FocusButton), "تركيز النافذة", StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetName(folder), "افتح مجلدًا", StringComparison.Ordinal),
            "The language selection did not refresh RTL, visible text, icon and accessible names in the native windows.");
        Require(ReferenceEquals(command, window.FocusButton.Command) && Equals(gesture, window.FocusShortcut.Gesture),
            "The language selection changed the bound action or keyboard gesture.");
    }

    private static async Task VerifyRestartAsync(DirectoryInfo assets, WorkspaceExplorerStore settings, RestoredState saved, CancellationToken token)
    {
        var stored = settings.Load()?.Snapshot();
        Require(stored is not null && stored.Selection == saved.Selection && stored.Expanded.Length == 2,
            $"The native workspace saved unexpected state ({stored?.Selection}, {stored?.Expanded.Length} expanded roots).");
        using var reopened = CreateSession(assets, settings);
        var window = new WorkbenchWindow(reopened);
        window.Show();
        try
        {
            await WaitAsync(() => reopened.ExplorerView.SelectedNode?.Address == saved.Selection, "restore selected file", token).ConfigureAwait(true);
            Require(reopened.View.WorkspaceId == saved.WorkspaceId && reopened.View.ViewKey != saved.ViewKey &&
                reopened.ExplorerView.TreeRoots.Count == 2 && reopened.ExplorerView.TreeRoots.All(root => root.IsExpanded) &&
                window.FlowDirection == FlowDirection.RightToLeft &&
                string.Equals(window.FocusButtonLabel.Text, "تركيز النافذة", StringComparison.Ordinal),
                "Reopening the native window lost language, workspace, Explorer expansion or selection.");
            Console.WriteLine($"Native RemoteControl verified localized text, icon, accessible names, unchanged shortcut, and RTL in docked and floating windows and restored language and Explorer state on reopen ({window.TryGetPlatformHandle()?.HandleDescriptor}).");
        }
        finally { await CloseAsync(window).ConfigureAwait(true); }
    }

    private static async Task CloseAsync(Window window)
    {
        if (!window.IsVisible)
            return;

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(object? sender, EventArgs args) => closed.TrySetResult();
        window.Closed += OnClosed;
        try
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(true);
        }
        finally { window.Closed -= OnClosed; }
    }

    private static void WriteLanguagePack(DirectoryInfo assets)
    {
        var locale = Directory.CreateDirectory(Path.Combine(assets.FullName, "locales", "ar")).FullName;
        File.WriteAllText(Path.Combine(locale, "manifest.json"), """
            {"locale":"ar","version":1,
              "text":{"command.workbench.view.focus.label":"تركيز النافذة","command.workspace.open-folder.label":"افتح مجلدًا"},
              "images":{"command.workbench.view.focus.icon":"focus.svg"}}
            """);
        File.WriteAllText(Path.Combine(locale, "focus.svg"), """
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18"><rect width="18" height="18" fill="#835fee"/></svg>
            """);
    }

    private static Task<TextEditor> EditorAsync(Window window, DocumentViewModel document, CancellationToken token) =>
        FindAsync(window, (TextEditor control) => ReferenceEquals(control.Document, document.Buffer) &&
            HasId(control, "document.source-editor"), token);

    private static bool HasId(Control control, string id) =>
        string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal);

    private static Window SeparateWindow(Window window, WorkbenchSession session)
    {
        var floating = session.Layout.Root.Windows?.SingleOrDefault()?.Host as Window;
        if (window.TryGetPlatformHandle() is { } primary && floating?.TryGetPlatformHandle() is { } detached && primary.Handle != detached.Handle)
            return floating;
        throw new InvalidOperationException("The dockable did not float into a separate native window.");
    }

    private static async Task ClickAsync(Window window, string id, CancellationToken token)
    {
        await FindAsync(window, (Control control) => HasId(control, id) && control.IsEffectivelyEnabled, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
    }

    private static async Task SendAsync(Window root, IReadOnlyList<RemoteInputEvent> input)
    {
        var sender = new RemoteControlInputDispatcher(new RootProvider(root),
            Options.Create(new AvaloniaRemoteControlOptions { AllowRemoteActions = true, AllowRemoteInput = true }),
            new AvaloniaUiThreadRemoteControlDispatcher(), NullLogger<RemoteControlInputDispatcher>.Instance);
        var result = await sender.SendInputAsync(input, "retro-ide-native-workflow").ConfigureAwait(true);
        Require(result.Succeeded, result.Message);
    }

    private static async Task<T> FindAsync<T>(Window window, Func<T, bool> match, CancellationToken token) where T : Control
    {
        T? control = null;
        await WaitAsync(() =>
        {
            window.UpdateLayout();
            control = window.GetVisualDescendants().OfType<T>().FirstOrDefault(candidate => candidate.IsEffectivelyVisible && match(candidate));
            return control is not null;
        }, $"render {typeof(T).Name}", token).ConfigureAwait(true);
        return control!;
    }

    private static async Task WaitAsync(Func<bool> condition, string operation, CancellationToken token)
    {
        try
        {
            while (!condition())
                await Task.Delay(25, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timed out waiting to {operation}.", error);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record RootProvider(Window Root) : IRemoteControlRootProvider
    {
        public Control GetRootControl() => Root;
    }

    private sealed record RestoredState(Guid WorkspaceId, Guid ViewKey, WorkspaceExplorer.Address Selection);
}
