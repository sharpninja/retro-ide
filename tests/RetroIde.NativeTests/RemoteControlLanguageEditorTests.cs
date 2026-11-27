using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlLanguageEditorTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-features; TEST-RETRO-014#ac-test-language-documents.</remarks>
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-native-language-editor-");
        try
        {
            const string original = "/* é😀 */ int value;\r\n";
            var file = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(file, original, token).ConfigureAwait(true);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await session.ExplorerView.OpenFolderCommand.ExecuteAsync(fixture.FullName).ConfigureAwait(true);
                var root = session.Explorer.Roots.Single();
                var address = new WorkspaceExplorer.Address(root.Id, "fixture.c");
                var source = await WorkspaceExplorer.ReadSourceAsync(session.Explorer.ResolveSource(address), token).ConfigureAwait(true);
                _ = session.Documents.OpenOrActivate(address, source, null) ?? throw new InvalidOperationException("Cannot open the fixture source.");
                await ConfigureAsync(session, root, token).ConfigureAwait(true);
                var view = session.Layout.CurrentSource ?? throw new InvalidOperationException("The source tab is not active.");
                var editor = await FindAsync<TextEditor>(window, "document.source-editor", token).ConfigureAwait(true);
                var offset = original.IndexOf("value", StringComparison.Ordinal);
                editor.CaretOffset = offset;

                await ClickAsync(window, "language.document.hover.toolbar", token).ConfigureAwait(true);
                var hover = await FindAsync<TextBlock>(window, "document.language.message", token).ConfigureAwait(true);
                await WaitAsync(() => hover.Text?.Contains(original.TrimEnd(), StringComparison.Ordinal) == true,
                    "render hover information", token).ConfigureAwait(true);

                await ClickAsync(window, "language.document.format.toolbar", token).ConfigureAwait(true);
                var preview = await FindAsync<SelectableTextBlock>(window, "document.language.preview", token).ConfigureAwait(true);
                if (preview.Text is not { } diff || !diff.Contains("- value", StringComparison.Ordinal) ||
                    !diff.Contains("+ formatted", StringComparison.Ordinal))
                    throw new InvalidOperationException("The formatting preview does not show the original and replacement text.");
                if (!string.Equals(editor.Text, original, StringComparison.Ordinal))
                    throw new InvalidOperationException("Opening a formatting preview edited the buffer.");
                await ClickAsync(window, "language.document.edit.apply.toolbar", token).ConfigureAwait(true);
                await WaitAsync(() => editor.Text == original.Replace("value", "formatted", StringComparison.Ordinal),
                    "apply reviewed formatting", token).ConfigureAwait(true);
                if (!view.IsDirty || !string.Equals(original, await File.ReadAllTextAsync(file, token).ConfigureAwait(true), StringComparison.Ordinal))
                    throw new InvalidOperationException("Applying a preview did not preserve the source on disk.");
                await ClickAsync(window, "document.undo.toolbar", token).ConfigureAwait(true);
                await WaitAsync(() => string.Equals(editor.Text, original, StringComparison.Ordinal), "undo formatting", token).ConfigureAwait(true);
                await VerifyNavigationAndActionsAsync(window, editor, original, offset, token).ConfigureAwait(true);
                await VerifyCompletionAsync(window, editor, original, offset, token).ConfigureAwait(true);
                Console.WriteLine("RemoteControl verified LSP hover, definition navigation, reviewed formatting and code actions, and completion insertion in AvaloniaEdit.");
            }
            finally { window.Close(); }
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task VerifyNavigationAndActionsAsync(Window window, TextEditor editor, string original, int offset, CancellationToken token)
    {
        var tools = await FindAsync<Expander>(window, "document.language.tools", token).ConfigureAwait(true);
        tools.IsExpanded = true;
        editor.CaretOffset = offset;
        await ClickAsync(window, "language.document.definition.toolbar", token).ConfigureAwait(true);
        await FindAsync<Button>(window, "language.document.result.open.toolbar", token).ConfigureAwait(true);
        editor.CaretOffset = 0;
        await ClickAsync(window, "language.document.result.open.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => editor.CaretOffset == offset, "navigate to the definition", token).ConfigureAwait(true);

        await ClickAsync(window, "language.document.code-actions.toolbar", token).ConfigureAwait(true);
        var results = await FindAsync<ListBox>(window, "document.language.results", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(results.SelectedItem?.ToString(), "Resolve fixture fix", StringComparison.Ordinal),
            "select the offered code action", token).ConfigureAwait(true);
        await ClickAsync(window, "language.document.result.open.toolbar", token).ConfigureAwait(true);
        var preview = await FindAsync<SelectableTextBlock>(window, "document.language.preview", token).ConfigureAwait(true);
        if (preview.Text?.Contains("+ fixed", StringComparison.Ordinal) != true ||
            !string.Equals(editor.Text, original, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolving the code action did not prepare a separate review.");
        await ClickAsync(window, "language.document.edit.apply.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(editor.Text, original.Replace("value", "fixed", StringComparison.Ordinal), StringComparison.Ordinal),
            "apply the reviewed code action", token).ConfigureAwait(true);
        await ClickAsync(window, "document.undo.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(editor.Text, original, StringComparison.Ordinal), "undo the code action", token).ConfigureAwait(true);
    }

    private static async Task VerifyCompletionAsync(Window window, TextEditor editor, string original, int offset, CancellationToken token)
    {
        editor.CaretOffset = offset + 3;
        await ClickAsync(window, "language.document.complete.toolbar", token).ConfigureAwait(true);
        var results = await FindAsync<ListBox>(window, "document.language.results", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(results.SelectedItem?.ToString(), "fixture completion", StringComparison.Ordinal),
            "select completion", token).ConfigureAwait(true);
        await ClickAsync(window, "language.document.result.open.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => string.Equals(editor.Text, original.Replace("value", "fixture completionue", StringComparison.Ordinal),
                StringComparison.Ordinal),
            "insert the selected completion", token).ConfigureAwait(true);
    }

    private static async Task ConfigureAsync(WorkbenchSession session, WorkspaceExplorer.Root root, CancellationToken token)
    {
        var executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "RetroIde.LspFixture.exe" : "RetroIde.LspFixture");
        var setup = new LanguageServerFeatures.ConfigureInput(root.Id, executable, [new("c", [".c"])],
            ["utf-8"], System.Text.Json.JsonSerializer.SerializeToElement(new { navigation = true }));
        var grant = new ActorGrant("native-language-editor", session.View.WorkspaceId, session.View.ViewKey, canMutate: true);
        var result = await session.Controller.DispatchAsync(session.Registry.Language.Configure, setup, session.View.Address,
            grant, Guid.NewGuid(), token).ConfigureAwait(true);
        if (!string.Equals(result.Code, "ok", StringComparison.Ordinal))
            throw new InvalidOperationException($"Language fixture configuration failed with {result.Code}.");
    }

    private static async Task ClickAsync(Window window, string id, CancellationToken token)
    {
        await FindAsync<Button>(window, id, token).ConfigureAwait(true);
        await WorkbenchRemoteControl.ClickAsync(window, id).ConfigureAwait(true);
    }

    private static async Task<T> FindAsync<T>(Window window, string id, CancellationToken token) where T : Control
    {
        T? control = null;
        await WaitAsync(() =>
        {
            window.UpdateLayout();
            control = window.GetVisualDescendants().OfType<T>().FirstOrDefault(candidate => candidate.IsEffectivelyVisible &&
                string.Equals(AutomationProperties.GetAutomationId(candidate), id, StringComparison.Ordinal));
            return control is not null;
        }, id, token).ConfigureAwait(true);
        return control!;
    }

    private static async Task WaitAsync(Func<bool> condition, string target, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (!condition())
                await Task.Delay(25, deadline.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Language editor verification timed out waiting for {target}.");
        }
    }
}
