using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Exercises negotiated hover, locations and target activation against dirty editor text.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-features; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaTheory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-32")]
    public async Task Language_navigation_uses_the_current_buffers_and_negotiated_positions(string encoding)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-lsp-navigation-");
        try
        {
            const string saved = "int saved;\n";
            const string live = "\r\n/* é😀 */ int live;\r\n";
            var file = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(file, saved, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var original = await OpenAsync(client, root);
            var edited = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = original, ["offset"] = 0, ["length"] = saved.Length, ["text"] = live,
            })).GetProperty("data");
            await ConfigureAsync(client, root, new { navigation = true }, encoding);
            var position = live.IndexOf("live", StringComparison.Ordinal);
            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["document"] = edited, ["offset"] = position };
            var hover = await client.CallAsync("language_hover", arguments);
            Assert.Equal("ok", Code(hover));
            Assert.Equal(live, hover.GetProperty("data").GetProperty("result").GetProperty("contents").GetProperty("value").GetString());

            var definition = await client.CallAsync("language_definition", arguments);
            Assert.Equal("ok", Code(definition));
            var result = definition.GetProperty("data");
            var target = Assert.Single(result.GetProperty("targets").EnumerateArray());
            Assert.Equal(new Uri(file).AbsoluteUri, target.GetProperty("uri").GetString());
            Assert.Equal(1, target.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
            var activation = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["document"] = edited, ["sessionId"] = result.GetProperty("server").GetProperty("sessionId"), ["target"] = target,
            };
            var opened = await client.ChangeAsync("language_open_location", activation);
            Assert.Equal(edited.GetProperty("viewKey").GetGuid(), opened.GetProperty("data").GetProperty("document").GetProperty("viewKey").GetGuid());
            Assert.Equal(position, opened.GetProperty("data").GetProperty("offset").GetInt32());
            var foreign = new Uri(Path.Combine(Path.GetTempPath(), "elsewhere.c")).AbsoluteUri;
            activation["target"] = new { uri = foreign, range = target.GetProperty("range") };
            activation["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
            Assert.Equal("invalid_language_location", Code(await client.CallAsync("language_open_location", activation)));

            var references = await client.CallAsync("language_references", arguments);
            Assert.Equal("ok", Code(references));
            Assert.Single(references.GetProperty("data").GetProperty("targets").EnumerateArray());
            arguments["includeDeclaration"] = false;
            var referencesWithoutDeclaration = await client.CallAsync("language_references", arguments);
            Assert.Empty(referencesWithoutDeclaration.GetProperty("data").GetProperty("targets").EnumerateArray());
            arguments.Remove("includeDeclaration");

            var stale = new Dictionary<string, object?>(arguments, StringComparer.Ordinal) { ["document"] = original };
            Assert.Equal("document_changed", Code(await client.CallAsync("language_hover", stale)));
            Assert.Equal(saved, await File.ReadAllTextAsync(file, token));
        }
        finally { fixture.Delete(recursive: true); }
    }
}
