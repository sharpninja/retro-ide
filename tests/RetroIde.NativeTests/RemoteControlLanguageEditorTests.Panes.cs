using System.Text.Json;
using Avalonia.Controls;
using AvaloniaEdit;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlLanguageEditorTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    internal static async Task RunPanesAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-native-language-panes-");
        try
        {
            const string sourceText = "ERROR\nint value;\n";
            var path = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(path, sourceText, token).ConfigureAwait(true);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await session.ExplorerView.OpenFolderCommand.ExecuteAsync(fixture.FullName).ConfigureAwait(true);
                var root = session.Explorer.Roots.Single();
                var address = new WorkspaceExplorer.Address(root.Id, "fixture.c");
                var source = await WorkspaceExplorer.ReadSourceAsync(session.Explorer.ResolveSource(address), token).ConfigureAwait(true);
                _ = session.Documents.OpenOrActivate(address, source, null) ?? throw new InvalidOperationException("Cannot open fixture.");
                await VerifyProfileCommandsAsync(window, session, fixture.FullName, token).ConfigureAwait(true);
                await VerifyProblemsAsync(window, session, sourceText, token).ConfigureAwait(true);
                if (!string.Equals(sourceText, await File.ReadAllTextAsync(path, token).ConfigureAwait(true), StringComparison.Ordinal))
                    throw new InvalidOperationException("The language pane workflow edited the disk file.");
                Console.WriteLine("RemoteControl configured, stopped, restarted, and removed a language server, and verified live Problems navigation and stale diagnostic clearance.");
                Console.WriteLine("RemoteControl displayed a faulted server and its error output, then restored unsaved editor state after restart.");
            }
            finally { window.Close(); }
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task VerifyProfileCommandsAsync(Window window, WorkbenchSession session, string fixture, CancellationToken token)
    {
        if (!session.Layout.CanFocus("language-servers") || !session.Layout.Focus("language-servers"))
            throw new InvalidOperationException("Cannot focus the Language Servers pane.");
        var rootChoice = await FindAsync<ComboBox>(window, "language.server.root", token).ConfigureAwait(true);
        await WaitAsync(() => rootChoice.Items.Count == 1, "list the workspace root", token).ConfigureAwait(true);
        rootChoice.SelectedIndex = 0;
        var executable = await FindAsync<TextBox>(window, "language.server.executable", token).ConfigureAwait(true);
        executable.Text = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "RetroIde.LspFixture.exe" : "RetroIde.LspFixture");
        (await FindAsync<TextBox>(window, "language.server.associations", token).ConfigureAwait(true)).Text = "c: .c";
        (await FindAsync<TextBox>(window, "language.server.arguments", token).ConfigureAwait(true)).Text = "[\"utf-8\"]";
        (await FindAsync<Expander>(window, "language.server.advanced", token).ConfigureAwait(true)).IsExpanded = true;
        var initialization = await FindAsync<TextBox>(window, "language.server.initialization-options", token).ConfigureAwait(true);
        initialization.Text = JsonSerializer.Serialize(new { crashOnFirstCompletion = true, traceDirectory = fixture });
        await WaitAsync(() => session.LanguageServersView.ConfigureCommand.CanExecute(null), "enable Configure and Start", token).ConfigureAwait(true);
        await ClickAsync(window, "language.server.configure.toolbar", token).ConfigureAwait(true);
        var list = await FindAsync<ListBox>(window, "language.server.profiles", token).ConfigureAwait(true);
        await WaitAsync(() => list.Items.Count == 1 && session.Controller.LanguageServers.List().Single().State is "running",
            "start the configured profile", token).ConfigureAwait(true);
        list.SelectedIndex = 0;
        var first = session.Controller.LanguageServers.List().Single().SessionId;
        await ClickAsync(window, "language.server.stop.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => session.Controller.LanguageServers.List().Single().State is "stopped", "stop the profile", token).ConfigureAwait(true);
        await WaitAsync(() => list.SelectedItem?.ToString()?.Contains("Stopped", StringComparison.Ordinal) == true,
            "display the stopped profile", token).ConfigureAwait(true);
        await ClickAsync(window, "language.server.restart.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => session.Controller.LanguageServers.List().Single() is { State: "running", SessionId: { } restarted } &&
            restarted != first, "restart the profile", token).ConfigureAwait(true);
    }

    private static async Task CrashAndRecoverAsync(Window window, WorkbenchSession session, TextEditor editor, ListBox problems, CancellationToken token)
    {
        await ClickAsync(window, "language.document.complete.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => problems.Items.Count == 0, "clear diagnostics from an exited server", token).ConfigureAwait(true);
        editor.Document.Insert(editor.Document.TextLength, "// edited while offline\n");
        editor.CaretOffset = editor.Document.TextLength;

        if (!session.Layout.Focus("language-servers"))
            throw new InvalidOperationException("Cannot inspect a faulted language server.");
        var profiles = await FindAsync<ListBox>(window, "language.server.profiles", token).ConfigureAwait(true);
        await WaitAsync(() => profiles.SelectedItem?.ToString()?.Contains("Faulted", StringComparison.Ordinal) == true,
            "display the faulted process", token).ConfigureAwait(true);
        await ClickAsync(window, "language.server.output.toolbar", token).ConfigureAwait(true);
        var output = await FindAsync<SelectableTextBlock>(window, "language.server.stderr", token).ConfigureAwait(true);
        await WaitAsync(() => output.Text?.Contains("Fixture exited during completion.", StringComparison.Ordinal) == true,
            "display server standard error", token).ConfigureAwait(true);
        await ClickAsync(window, "language.server.restart.toolbar", token).ConfigureAwait(true);
        if (!session.Layout.Focus("problems"))
            throw new InvalidOperationException("Cannot read diagnostics after restart.");
        await WaitAsync(() => session.Controller.LanguageServers.List().Single().State is "running",
            "restart the process while switching panes", token).ConfigureAwait(true);
        await WaitAsync(() => session.ProblemsView.Items.Count == 1, "resynchronize diagnostics after restart", token).ConfigureAwait(true);
        if (editor.Text?.EndsWith("// edited while offline\n", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Restart discarded changes made while the server was unavailable.");
    }

    private static async Task VerifyProblemsAsync(Window window, WorkbenchSession session, string text, CancellationToken token)
    {
        if (!session.Layout.Focus("problems"))
            throw new InvalidOperationException("Cannot focus Problems.");
        var problems = await FindAsync<ListBox>(window, "language.problems.items", token).ConfigureAwait(true);
        await WaitAsync(() => problems.Items.Count == 1 && problems.Items[0]?.ToString()?.Contains("Fixture error marker.", StringComparison.Ordinal) == true,
            "display the language diagnostic in Problems", token).ConfigureAwait(true);
        var editor = await FindAsync<TextEditor>(window, "document.source-editor", token).ConfigureAwait(true);
        editor.CaretOffset = text.Length;
        await CrashAndRecoverAsync(window, session, editor, problems, token).ConfigureAwait(true);
        problems = await FindAsync<ListBox>(window, "language.problems.items", token).ConfigureAwait(true);
        await WaitAsync(() => problems.Items.Count == 1, "redisplay the restored diagnostic", token).ConfigureAwait(true);
        problems.SelectedIndex = 0;
        await ClickAsync(window, "language.problem.open.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => editor.CaretOffset == 0, "open the diagnostic at its source position", token).ConfigureAwait(true);
        editor.Document.Replace(0, 5, "CLEAN");
        await WaitAsync(() => problems.Items.Count == 0, "clear the superseded diagnostic", token).ConfigureAwait(true);
        if (!session.Layout.Focus("language-servers"))
            throw new InvalidOperationException("Cannot return to Language Servers.");
        await ClickAsync(window, "language.server.remove.toolbar", token).ConfigureAwait(true);
        await WaitAsync(() => session.Controller.LanguageServers.List().Length == 0 &&
            session.Explorer.Snapshot().LanguageProfiles?.Length is 0 or null, "remove the profile and its saved configuration", token).ConfigureAwait(true);
    }
}
