using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Exercises scoped dynamic capability registration, malformed batches, unregistration and static fallback.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaFact]
    public async Task Dynamic_capabilities_follow_the_server_and_document_selector()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-dynamic-capabilities-");
        try
        {
            const string text = "int source;\n";
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "src", "nested"));
            var documentsDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "docs"));
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory.FullName, "source.c"), text, token);
            await File.WriteAllTextAsync(Path.Combine(documentsDirectory.FullName, "source.c"), text, token);
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory.FullName, "xource.c"), text, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var source = await OpenCapabilityFixtureAsync(client, root, "src/nested/source.c");
            var peer = await OpenCapabilityFixtureAsync(client, root, "docs/source.c");
            var excluded = await OpenCapabilityFixtureAsync(client, root, "src/nested/xource.c");
            await ConfigureAsync(client, root, new { navigation = true, dynamicDocuments = true, traceDirectory = fixture.FullName });
            Assert.Equal("True", await File.ReadAllTextAsync(Path.Combine(fixture.FullName, "dynamic-hover-client-capability"), token));
            await VerifyDynamicCapabilityCycleAsync(client, source, peer, excluded, fixture.FullName, token);
            await VerifyRelativeCapabilityAsync(client, source, peer, fixture.FullName, token);
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task VerifyDynamicCapabilityCycleAsync(Connection client, JsonElement source, JsonElement peer,
        JsonElement excluded, string fixture, CancellationToken token)
    {
        const string hover = "textDocument/hover";
        Assert.DoesNotContain(hover, await ReadDocumentCapabilitiesAsync(client, source));
        Assert.Contains("textDocument/definition", await ReadDocumentCapabilitiesAsync(client, source));
        Assert.Equal("language_feature_unavailable", Code(await HoverCapabilityFixtureAsync(client, source)));

        await CompleteAsync(client, source, 4);
        await AssertCapabilityResponseAsync(fixture, "invalid", accepted: false, token);
        Assert.DoesNotContain(hover, await ReadDocumentCapabilitiesAsync(client, source));

        await CompleteAsync(client, source, 4);
        await AssertCapabilityResponseAsync(fixture, "registered", accepted: true, token);
        Assert.Contains(hover, await ReadDocumentCapabilitiesAsync(client, source));
        Assert.DoesNotContain(hover, await ReadDocumentCapabilitiesAsync(client, peer));
        Assert.DoesNotContain(hover, await ReadDocumentCapabilitiesAsync(client, excluded));
        Assert.Equal("ok", Code(await HoverCapabilityFixtureAsync(client, source)));
        Assert.Equal("language_feature_unavailable", Code(await HoverCapabilityFixtureAsync(client, peer)));
        Assert.Equal("language_feature_unavailable", Code(await HoverCapabilityFixtureAsync(client, excluded)));

        await CompleteAsync(client, source, 4);
        await AssertCapabilityResponseAsync(fixture, "unregistered", accepted: true, token);
        Assert.DoesNotContain(hover, await ReadDocumentCapabilitiesAsync(client, source));
        Assert.Contains("textDocument/definition", await ReadDocumentCapabilitiesAsync(client, source));
        Assert.Equal("language_feature_unavailable", Code(await HoverCapabilityFixtureAsync(client, source)));
    }

    private static async Task VerifyRelativeCapabilityAsync(Connection client, JsonElement source, JsonElement peer,
        string fixture, CancellationToken token)
    {
        await CompleteAsync(client, source, 4);
        await AssertCapabilityResponseAsync(fixture, "relative", accepted: true, token);
        var http = await ReadDocumentCapabilitiesAsync(client, source);
        Assert.Contains("textDocument/hover", http);
        Assert.DoesNotContain("textDocument/hover", await ReadDocumentCapabilitiesAsync(client, peer));

        await using var stdio = await client.ConnectStdioAsync(Path.Combine(fixture, "capability-stdio.json"));
        var stdioReply = await client.CallAsync("language_get_document_capabilities", new(StringComparer.Ordinal)
            { ["document"] = source }, stdio);
        Assert.Equal("ok", Code(stdioReply));
        Assert.Equal(http, stdioReply.GetProperty("data").GetProperty("methods").EnumerateArray().Select(item => item.GetString()).ToArray());

        await client.ChangeAsync("language_stop_server", new(StringComparer.Ordinal) { ["rootId"] = source.GetProperty("rootId") });
        Assert.Empty(await ReadDocumentCapabilitiesAsync(client, source));
    }

    private static async Task AssertCapabilityResponseAsync(string fixture, string step, bool accepted, CancellationToken token)
    {
        var path = Path.Combine(fixture, $"fixture-capabilities-{step}");
        await WaitForFileAsync(path, token);
        using var response = await ReadCapabilityTraceAsync(path, token);
        Assert.Equal(accepted, response.RootElement.TryGetProperty("result", out _));
        if (!accepted)
            Assert.Equal(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private static async Task<JsonDocument> ReadCapabilityTraceAsync(string path, CancellationToken token)
    {
        while (true)
        {
            try
            {
                // The fixture publishes a complete file by renaming it in this directory.
                await using var trace = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
                return await JsonDocument.ParseAsync(trace, cancellationToken: token);
            }
            catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(25, token);
            }
        }
    }

    private static async Task<JsonElement> OpenCapabilityFixtureAsync(Connection client, Guid root, string path) =>
        (await client.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = path })).GetProperty("data");

    private static async Task<string[]> ReadDocumentCapabilitiesAsync(Connection client, JsonElement document)
    {
        var reply = await client.CallAsync("language_get_document_capabilities", new(StringComparer.Ordinal) { ["document"] = document });
        Assert.Equal("ok", Code(reply));
        return reply.GetProperty("data").GetProperty("methods").EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static Task<JsonElement> HoverCapabilityFixtureAsync(Connection client, JsonElement document) =>
        client.CallAsync("language_hover", new(StringComparer.Ordinal) { ["document"] = document, ["offset"] = 4 });
}
