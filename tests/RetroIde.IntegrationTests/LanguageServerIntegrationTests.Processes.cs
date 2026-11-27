using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Queries an installed server through Retro IDE's stdio attachment after editing an unsaved source buffer.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaTheory]
    [InlineData("clangd", "RETROIDE_TEST_CLANGD", "fixture.c", "c")]
    [InlineData("rust-analyzer", "RETROIDE_TEST_RUST_ANALYZER", "lib.rs", "rust")]
    public async Task Installed_language_server_reports_symbols_from_unsaved_source(string server, string variable,
        string filename, string language)
    {
        var executable = Environment.GetEnvironmentVariable(variable);
        Assert.SkipUnless(executable is not null && Path.IsPathFullyQualified(executable) && File.Exists(executable),
            $"Set {variable} to the installed {server} executable.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-real-language-");
        try
        {
            var path = Path.Combine(fixture.FullName, filename);
            var code = language == "rust"
                ? "pub fn disk_symbol_only() -> i32 { 24 }\n"
                : "int disk_symbol_only(void) { return 24; }\n";
            await File.WriteAllTextAsync(path, code, token).ConfigureAwait(true);
            var options = language == "rust" ? await RustConfigurationAsync(fixture.FullName, path, token).ConfigureAwait(true) : null;
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var http = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
            var rootId = await http.AddRootAsync(fixture.FullName).ConfigureAwait(true);
            var start = await http.ChangeAsync("language_configure_server", new(StringComparer.Ordinal)
            {
                ["rootId"] = rootId, ["executable"] = executable,
                ["arguments"] = language == "c" ? new[] { "--background-index=0" } : Array.Empty<string>(),
                ["languages"] = new[] { new { languageId = language, extensions = new[] { Path.GetExtension(filename) } } },
                ["initializationOptions"] = options,
            }).ConfigureAwait(true);
            Assert.Equal("utf-8", start.GetProperty("data").GetProperty("positionEncoding").GetString());
            var opened = (await http.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
                { ["rootId"] = rootId, ["relativePath"] = filename }).ConfigureAwait(true)).GetProperty("data");
            var edit = await http.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = opened, ["offset"] = code.IndexOf("disk_symbol_only", StringComparison.Ordinal),
                ["length"] = "disk_symbol_only".Length, ["text"] = "unsaved_symbol",
            }).ConfigureAwait(true);
            await using var stdio = await http.ConnectStdioAsync(Path.Combine(fixture.FullName, "lsp-stdio.json")).ConfigureAwait(true);
            var symbols = await http.CallAsync("language_document_symbols", new(StringComparer.Ordinal)
                { ["document"] = edit.GetProperty("data") }, stdio).ConfigureAwait(true);
            Assert.Equal("ok", Code(symbols));
            Assert.Contains(symbols.GetProperty("data").GetProperty("result").EnumerateArray(),
                symbol => string.Equals(symbol.GetProperty("name").GetString(), "unsaved_symbol", StringComparison.Ordinal));
            Assert.Equal(code, await File.ReadAllTextAsync(path, token).ConfigureAwait(true));
        }
        finally { fixture.Delete(recursive: true); }
    }

    /// <summary>Observes LSP cancellation on the wire when a newer editor revision supersedes an in-flight request.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaFact]
    public async Task Editing_the_buffer_cancels_the_superseded_completion_in_the_native_server()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-language-stale-");
        try
        {
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var http = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(fixture.FullName, "fixture.c"), "int old;\n", token).ConfigureAwait(true);
            var rootId = await http.AddRootAsync(fixture.FullName).ConfigureAwait(true);
            var document = await OpenAsync(http, rootId).ConfigureAwait(true);
            await ConfigureAsync(http, rootId, new { deferCompletions = true, traceDirectory = fixture.FullName }).ConfigureAwait(true);

            var pending = http.CallAsync("language_complete", new(StringComparer.Ordinal) { ["document"] = document, ["offset"] = 4 });
            var held = Path.Combine(fixture.FullName, "completion-held");
            await WaitForFileAsync(held, token).ConfigureAwait(true);
            await http.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = document, ["offset"] = 4, ["length"] = 3, ["text"] = "updated",
            }).ConfigureAwait(true);
            Assert.Equal("document_changed", Code(await pending.ConfigureAwait(true)));
            var cancellation = Path.Combine(fixture.FullName, "cancel-received");
            await WaitForFileAsync(cancellation, token).ConfigureAwait(true);
            Assert.Equal(await File.ReadAllTextAsync(held, token).ConfigureAwait(true),
                await File.ReadAllTextAsync(cancellation, token).ConfigureAwait(true));
        }
        finally { fixture.Delete(recursive: true); }
    }

    /// <summary>Keeps the editor usable after an unexpected server exit and restores the current buffer on restart.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaFact]
    public async Task An_exited_language_server_can_restart_without_losing_editor_changes()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-language-recovery-");
        try
        {
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var http = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
            const string saved = "int original;\n";
            var file = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(file, saved, token).ConfigureAwait(true);
            var rootId = await http.AddRootAsync(fixture.FullName).ConfigureAwait(true);
            var document = await OpenAsync(http, rootId).ConfigureAwait(true);
            await ConfigureAsync(http, rootId, new { crashOnFirstCompletion = true, traceDirectory = fixture.FullName }).ConfigureAwait(true);
            var pending = await http.CallAsync("language_complete", new(StringComparer.Ordinal)
                { ["document"] = document, ["offset"] = 4 }).ConfigureAwait(true);
            Assert.Equal("language_server_failed", Code(pending));
            var servers = await http.CallAsync("language_list_servers").ConfigureAwait(true);
            Assert.Equal("faulted", Assert.Single(servers.GetProperty("data").EnumerateArray()).GetProperty("state").GetString());

            const string dirty = "int edit_while_offline;\n";
            var edited = (await http.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = document, ["offset"] = 0, ["length"] = saved.Length, ["text"] = dirty,
            }).ConfigureAwait(true)).GetProperty("data");
            await http.ChangeAsync("language_restart_server", new(StringComparer.Ordinal) { ["rootId"] = rootId }).ConfigureAwait(true);
            AssertFixtureReply(await CompleteAsync(http, edited, 4).ConfigureAwait(true), dirty, 4, expectedVersion: 1);
            Assert.Equal(saved, await File.ReadAllTextAsync(file, token).ConfigureAwait(true));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task<object> RustConfigurationAsync(string root, string source, CancellationToken token)
    {
        var manifest = Path.Combine(root, "rust-project.json");
        var project = new
        {
            crates = new[] { new { root_module = source, edition = "2024", deps = Array.Empty<object>(),
                is_workspace_member = true, display_name = "fixture" } },
        };
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(project), token).ConfigureAwait(false);
        return new { linkedProjects = new[] { manifest }, checkOnSave = false, cargo = new { buildScripts = new { enable = false } } };
    }

    private static async Task WaitForFileAsync(string path, CancellationToken token)
    {
        while (!File.Exists(path))
            await Task.Delay(20, token).ConfigureAwait(false);
    }
}
