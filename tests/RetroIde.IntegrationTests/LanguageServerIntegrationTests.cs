using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the real language-server process through scoped Retro IDE tools.</summary>
public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Preserves dirty editor text, Unicode positions, diagnostics, and identity across restart.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaTheory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-32")]
    public async Task Fixture_negotiates_positions_uses_dirty_buffers_and_reopens_them_on_restart(string encoding)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var token = deadline.Token;
        var root = Directory.CreateTempSubdirectory("retro-ide-language-");
        try
        {
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var http = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
            var file = Path.Combine(root.FullName, "fixture.c");
            await File.WriteAllTextAsync(file, "int saved_only;\n", token).ConfigureAwait(true);
            var rootId = await http.AddRootAsync(root.FullName).ConfigureAwait(true);
            var initial = await OpenAsync(http, rootId).ConfigureAwait(true);
            await ConfigureAsync(http, rootId, encoding: encoding).ConfigureAwait(true);
            var clean = await CompleteAsync(http, initial, 4).ConfigureAwait(true);
            Assert.Equal("int saved_only;\n", clean.GetProperty("result")[0].GetProperty("detail").GetString());
            var server = clean.GetProperty("server");
            Assert.Equal(encoding, server.GetProperty("positionEncoding").GetString());
            var sessionId = server.GetProperty("sessionId").GetGuid();

            const string dirtyText = "ERROR Café 😀next\r\nsecond line\n";
            var edit = await http.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = initial, ["offset"] = 0, ["length"] = "int saved_only;\n".Length, ["text"] = dirtyText,
            }).ConfigureAwait(true);
            var dirty = edit.GetProperty("data");
            var dirtyOffset = dirtyText.IndexOf("next", StringComparison.Ordinal);
            var ready = await CompleteAsync(http, dirty, dirtyOffset).ConfigureAwait(true);
            AssertFixtureReply(ready, dirtyText, dirtyOffset, expectedVersion: 2);
            Assert.Equal(sessionId, ready.GetProperty("server").GetProperty("sessionId").GetGuid());
            var diagnostics = await http.CallAsync("language_get_diagnostics", new(StringComparer.Ordinal)
                { ["document"] = dirty }).ConfigureAwait(true);
            Assert.Equal("ok", Code(diagnostics));
            var diagnostic = Assert.Single(diagnostics.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal("Fixture error marker.", diagnostic.GetProperty("message").GetString());

            await http.ChangeAsync("language_restart_server", new(StringComparer.Ordinal) { ["rootId"] = rootId }).ConfigureAwait(true);
            var restarted = await CompleteAsync(http, dirty, dirtyOffset).ConfigureAwait(true);
            Assert.NotEqual(sessionId, restarted.GetProperty("server").GetProperty("sessionId").GetGuid());
            AssertFixtureReply(restarted, dirtyText, dirtyOffset, expectedVersion: 1);
            Assert.Equal("int saved_only;\n", await File.ReadAllTextAsync(file, token).ConfigureAwait(true));
            await http.ChangeAsync("language_stop_server", new(StringComparer.Ordinal) { ["rootId"] = rootId }).ConfigureAwait(true);
            var servers = await http.CallAsync("language_list_servers").ConfigureAwait(true);
            Assert.Equal("stopped", Assert.Single(servers.GetProperty("data").EnumerateArray()).GetProperty("state").GetString());
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task ConfigureAsync(Connection http, Guid rootId, object? initializationOptions = null,
        string encoding = "utf-8", string? profileId = null, string extension = ".c", object? settings = null)
    {
        await http.ChangeAsync("language_configure_server", ConfigurationArguments(rootId, initializationOptions, encoding,
            profileId, extension, settings)).ConfigureAwait(true);
    }

    private static Dictionary<string, object?> ConfigurationArguments(Guid rootId, object? initializationOptions = null,
        string encoding = "utf-8", string? profileId = null, string extension = ".c", object? settings = null)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(host) || !Path.IsPathFullyQualified(host) || !File.Exists(host))
        {
            var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var dotnet = Path.GetFullPath("../../..", runtime);
            host = Path.Combine(dotnet, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        }
        var fixture = Path.Combine(AppContext.BaseDirectory, "RetroIde.LspFixture.dll");
        Assert.True(File.Exists(host));
        Assert.True(File.Exists(fixture));
        return new(StringComparer.Ordinal)
        {
            ["rootId"] = rootId,
            ["executable"] = host,
            ["arguments"] = new[] { fixture, encoding },
            ["languages"] = new[] { new { languageId = extension[1..], extensions = new[] { extension } } },
            ["profileId"] = profileId,
            ["initializationOptions"] = initializationOptions,
            ["settings"] = settings,
        };
    }

    private static async Task<JsonElement> OpenAsync(Connection http, Guid root) =>
        (await http.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = "fixture.c" }).ConfigureAwait(true)).GetProperty("data");

    private static async Task<JsonElement> CompleteAsync(Connection http, JsonElement document, int offset)
    {
        var result = await http.CallAsync("language_complete", new(StringComparer.Ordinal)
            { ["document"] = document, ["offset"] = offset }).ConfigureAwait(true);
        Assert.Equal("ok", Code(result));
        return result.GetProperty("data");
    }

    private static void AssertFixtureReply(JsonElement reply, string text, int offset, int expectedVersion)
    {
        var completion = reply.GetProperty("result")[0];
        Assert.Equal(text, completion.GetProperty("detail").GetString());
        var data = completion.GetProperty("data");
        Assert.Equal(expectedVersion, data.GetProperty("version").GetInt32());
        Assert.Equal(offset, data.GetProperty("offset").GetInt32());
    }

    private static string? Code(JsonElement result) => result.GetProperty("code").GetString();
}
