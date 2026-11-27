using System.Text.Json;
using Avalonia.Controls;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlLanguageEditorTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-features.</remarks>
    internal static async Task RunCapabilitiesAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-native-capabilities-");
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(fixture.FullName, "src", "nested"));
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "source.c"), "int source;\n", token).ConfigureAwait(true);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await session.ExplorerView.OpenFolderCommand.ExecuteAsync(fixture.FullName).ConfigureAwait(true);
                var root = session.Explorer.Roots.Single();
                var address = new WorkspaceExplorer.Address(root.Id, Path.Combine("src", "nested", "source.c"));
                var source = await WorkspaceExplorer.ReadSourceAsync(session.Explorer.ResolveSource(address), token).ConfigureAwait(true);
                _ = session.Documents.OpenOrActivate(address, source, null) ?? throw new InvalidOperationException("Cannot open the capability fixture.");

                var hover = await FindAsync<Button>(window, "language.document.hover.toolbar", token).ConfigureAwait(true);
                var complete = await FindAsync<Button>(window, "language.document.complete.toolbar", token).ConfigureAwait(true);
                if (hover.IsEffectivelyEnabled || complete.IsEffectivelyEnabled)
                    throw new InvalidOperationException("The unconfigured editor enabled language operations.");

                await ConfigureDynamicFixtureAsync(session, root, token).ConfigureAwait(true);
                await WaitAsync(() => complete.IsEffectivelyEnabled, "enable static completion", token).ConfigureAwait(true);
                await VerifyCapabilityCommandsAsync(window, hover, complete, fixture.FullName, token).ConfigureAwait(true);
                Console.WriteLine("RemoteControl verified editor commands follow static and dynamic language capabilities.");
            }
            finally { window.Close(); }
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task VerifyCapabilityCommandsAsync(Window window, Button hover, Button complete, string fixture, CancellationToken token)
    {
        const string completion = "language.document.complete.toolbar";
        if (hover.IsEffectivelyEnabled)
            throw new InvalidOperationException("Hover was enabled before it was registered.");

        await ClickAsync(window, completion, token).ConfigureAwait(true);
        await WaitAsync(() => File.Exists(Path.Combine(fixture, "fixture-capabilities-invalid")), "reject the invalid capability batch", token)
            .ConfigureAwait(true);
        if (hover.IsEffectivelyEnabled)
            throw new InvalidOperationException("The invalid batch partially enabled hover.");

        await ClickAsync(window, completion, token).ConfigureAwait(true);
        await WaitAsync(() => hover.IsEffectivelyEnabled, "enable dynamically registered hover", token).ConfigureAwait(true);
        await ClickAsync(window, "language.document.hover.toolbar", token).ConfigureAwait(true);
        var message = await FindAsync<TextBlock>(window, "document.language.message", token).ConfigureAwait(true);
        await WaitAsync(() => message.Text?.Contains("int source;", StringComparison.Ordinal) == true, "render registered hover", token)
            .ConfigureAwait(true);

        await ClickAsync(window, completion, token).ConfigureAwait(true);
        await WaitAsync(() => !hover.IsEffectivelyEnabled, "disable withdrawn hover", token).ConfigureAwait(true);
        await WaitAsync(() => complete.IsEffectivelyEnabled, "retain independent completion", token).ConfigureAwait(true);
    }

    private static async Task ConfigureDynamicFixtureAsync(WorkbenchSession session, WorkspaceExplorer.Root root, CancellationToken token)
    {
        var executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "RetroIde.LspFixture.exe" : "RetroIde.LspFixture");
        var setup = new LanguageServerFeatures.ConfigureInput(root.Id, executable, [new("c", [".c"])], ["utf-8"],
            JsonSerializer.SerializeToElement(new { navigation = true, dynamicDocuments = true, traceDirectory = root.Path }));
        var grant = new ActorGrant("native-capabilities", session.View.WorkspaceId, session.View.ViewKey, canMutate: true);
        var result = await session.Controller.DispatchAsync(session.Registry.Language.Configure, setup, session.View.Address,
            grant, Guid.NewGuid(), token).ConfigureAwait(true);
        if (result.Code is not "ok")
            throw new InvalidOperationException($"Could not configure the dynamic fixture with {result.Code}.");
    }
}
