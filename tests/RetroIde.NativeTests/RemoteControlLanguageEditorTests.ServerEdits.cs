using System.Text.Json;
using Avalonia.Controls;
using AvaloniaEdit;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlLanguageEditorTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    internal static async Task RunServerEditsAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-native-server-edits-");
        try
        {
            const string original = "/* é😀 */ int value;\r\n";
            var source = Path.Combine(fixture.FullName, "fixture.c");
            var peer = Path.Combine(fixture.FullName, "peer.c");
            await File.WriteAllTextAsync(source, original, token).ConfigureAwait(true);
            await File.WriteAllTextAsync(peer, "int peer;\n", token).ConfigureAwait(true);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await OpenServerEditFixtureAsync(session, source, peer, token).ConfigureAwait(true);
                var editor = await FindAsync<TextEditor>(window, "document.source-editor", token).ConfigureAwait(true);
                var document = session.Layout.CurrentSource ?? throw new InvalidOperationException("The source buffer did not open.");
                editor.CaretOffset = original.IndexOf("value", StringComparison.Ordinal);
                await ClickAsync(window, "language.document.complete.toolbar", token).ConfigureAwait(true);
                await ClickAsync(window, "language.server-edits.indicator", token).ConfigureAwait(true);
                var preview = await FindAsync<SelectableTextBlock>(window, "language.server-edits.preview", token).ConfigureAwait(true);
                if (preview.Text is not { } text || !text.Contains("- value", StringComparison.Ordinal) ||
                    !text.Contains("+ server_value", StringComparison.Ordinal) || !text.Contains("peer.c", StringComparison.Ordinal) ||
                    !string.Equals(document.Buffer.Text, original, StringComparison.Ordinal))
                    throw new InvalidOperationException("The pending server edit did not show a preview of both buffers.");

                await ClickAsync(window, "language.server-edits.apply.toolbar", token).ConfigureAwait(true);
                await WaitAsync(() => document.Buffer.Text.Contains("server_value", StringComparison.Ordinal) &&
                    session.Layout.CurrentSource is { } peerView && peerView.Buffer.Text.StartsWith("// server edit", StringComparison.Ordinal),
                    "apply the reviewed workspace edit", token).ConfigureAwait(true);

                var responseFile = Path.Combine(fixture.FullName, "apply-edit-response");
                await WaitAsync(() => File.Exists(responseFile), "send the LSP applyEdit response", token).ConfigureAwait(true);
                using var response = JsonDocument.Parse(await File.ReadAllTextAsync(responseFile, token).ConfigureAwait(true));
                if (!response.RootElement.GetProperty("result").GetProperty("applied").GetBoolean() ||
                    !string.Equals(original, await File.ReadAllTextAsync(source, token).ConfigureAwait(true), StringComparison.Ordinal))
                    throw new InvalidOperationException("Server edit applied without preserving the source on disk.");
                Console.WriteLine("RemoteControl reviewed and applied a server-originated multi-file workspace edit without writing source files.");
            }
            finally { window.Close(); }
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task OpenServerEditFixtureAsync(WorkbenchSession session, string source, string peer, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(source)!;
        await session.ExplorerView.OpenFolderCommand.ExecuteAsync(directory).ConfigureAwait(true);
        var root = session.Explorer.Roots.Single();
        var address = new WorkspaceExplorer.Address(root.Id, Path.GetFileName(source));
        var file = await WorkspaceExplorer.ReadSourceAsync(session.Explorer.ResolveSource(address), token).ConfigureAwait(true);
        _ = session.Documents.OpenOrActivate(address, file, null) ?? throw new InvalidOperationException("Cannot open the source fixture.");
        var executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "RetroIde.LspFixture.exe" : "RetroIde.LspFixture");
        var setup = new LanguageServerFeatures.ConfigureInput(root.Id, executable, [new("c", [".c"])], ["utf-8"],
            JsonSerializer.SerializeToElement(new { serverEdit = "apply", serverEditPeer = new Uri(peer).AbsoluteUri, traceDirectory = directory }));
        var grant = new ActorGrant("native-server-workspace-edit", session.View.WorkspaceId, session.View.ViewKey, canMutate: true);
        var result = await session.Controller.DispatchAsync(session.Registry.Language.Configure, setup, session.View.Address,
            grant, Guid.NewGuid(), token).ConfigureAwait(true);
        if (result.Code is not "ok")
            throw new InvalidOperationException($"Could not configure the workspace edit fixture with {result.Code}.");
    }
}
