using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RetroIde.Application;
using RetroIde.Application.Workspaces;

namespace RetroIde.NativeTests;

internal static class Phase02AttachmentTests
{
    private const string FileName = "same name.asm";
    private const string Original = "move 1\nkeep";
    private const string Expected = "load 2\nkeep";

    internal static async Task RunAsync(McpClient http, McpClient stdio, McpClient other, string fixture, CancellationToken token)
    {
        await VerifyDiscoveryAsync(http, stdio, token).ConfigureAwait(false);
        await ExerciseAsync(http, stdio, other, fixture, "folder", "ar", token).ConfigureAwait(false);
        await ExerciseAsync(stdio, http, other, fixture, "project", "fr-CA", token).ConfigureAwait(false);

        var otherRoots = await QueryAsync(other, "workspace_list_roots", null, token).ConfigureAwait(false);
        Require(otherRoots.GetArrayLength() == 0, "The unselected production workbench received another workspace's roots.");
        Console.WriteLine("Both production MCP transports exercised Explorer, editor, and language operations. Each observed the other's live context, rejected stale or foreign requests, and saved only the intended source.");
    }

    internal static async Task VerifyReopenAsync(McpClient client, CancellationToken token)
    {
        var roots = await QueryAsync(client, "workspace_list_roots", null, token).ConfigureAwait(false);
        var language = await QueryAsync(client, "workbench_get_display_language", null, token).ConfigureAwait(false);
        Require(roots.GetArrayLength() == 2 &&
            roots.EnumerateArray().Select(root => root.GetProperty("kind").GetString()).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["Folder", "Project"]) &&
            string.Equals(language.GetProperty("locale").GetString(), "fr-CA", StringComparison.Ordinal),
            "The restarted production workbench did not recover its mixed roots and saved language.");
    }

    private static async Task VerifyDiscoveryAsync(McpClient http, McpClient stdio, CancellationToken token)
    {
        var direct = await http.ListToolsAsync(cancellationToken: token).ConfigureAwait(false);
        var bridged = (await stdio.ListToolsAsync(cancellationToken: token).ConfigureAwait(false))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        string[] expected =
        [
            "workspace_open_root", "workspace_list_roots", "workspace_list_children", "workspace_set_expanded",
            "workspace_select_entry", "workspace_open_file", "workspace_reveal_entry", "editor_get_document",
            "editor_edit_text", "editor_find_text", "editor_replace_text", "editor_undo", "editor_redo",
            "editor_save_document", "editor_close_document", "workbench_get_display_language", "workbench_set_display_language",
        ];
        Require(direct.Count == bridged.Count, "The production attachments advertised different tool counts.");
        foreach (var name in expected)
        {
            var left = direct.Single(tool => string.Equals(tool.Name, name, StringComparison.Ordinal)).ProtocolTool;
            Require(bridged.TryGetValue(name, out var tool) &&
                JsonElement.DeepEquals(left.InputSchema, tool.ProtocolTool.InputSchema) &&
                left.OutputSchema is { } output && tool.ProtocolTool.OutputSchema is { } bridgedOutput &&
                JsonElement.DeepEquals(output, bridgedOutput), $"The production SDK clients disagree on the schemas for {name}.");
        }
    }

    private static async Task ExerciseAsync(McpClient writer, McpClient reader, McpClient other,
        string fixture, string kind, string locale, CancellationToken token)
    {
        var directory = Directory.CreateDirectory(Path.Combine(fixture, "sources", kind));
        var path = Path.Combine(directory.FullName, FileName);
        await File.WriteAllTextAsync(path, Original, token).ConfigureAwait(false);
        var staleContext = await ReadContextAsync(writer, token).ConfigureAwait(false);
        var root = await WriteAsync(writer, "workspace_open_root", new { path = directory.FullName, kind }, token).ConfigureAwait(false);
        var rootId = Data(root).GetProperty("id").GetGuid();
        var roots = await QueryAsync(reader, "workspace_list_roots", null, token).ConfigureAwait(false);
        Require(roots.EnumerateArray().Any(entry => entry.GetProperty("id").GetGuid() == rootId),
            $"The paired reader cannot see the {kind} root.");
        var branch = await QueryAsync(reader, "workspace_list_children", new { rootId, relativePath = "" }, token).ConfigureAwait(false);
        Require(branch.GetProperty("isAvailable").GetBoolean() &&
            string.Equals(branch.GetProperty("entries").EnumerateArray().Single().GetProperty("name").GetString(), FileName, StringComparison.Ordinal),
            $"The paired reader cannot browse the {kind} root.");
        await WriteAsync(writer, "workspace_set_expanded", new { rootId, relativePath = "", isExpanded = true }, token).ConfigureAwait(false);
        await WriteAsync(writer, "workspace_select_entry", new { rootId, relativePath = FileName }, token).ConfigureAwait(false);
        var opened = await WriteAsync(writer, "workspace_open_file", new { rootId, relativePath = FileName, position = new { line = 2, column = 1 } },
            token).ConfigureAwait(false);
        var original = Document(Data(opened));
        Require(original.RootId == rootId && original.Revision == 0 && original.ViewKey != Guid.Empty, "Open returned a mismatched document context.");

        await ExpectAsync(other, "editor_get_document", new { viewKey = original.ViewKey }, "view_closed", token).ConfigureAwait(false);
        await ExpectAsync(writer, "workbench_set_display_language", new { locale, context = staleContext }, "stale_revision", token).ConfigureAwait(false);
        await VerifyDocumentAsync(writer, reader, original, path, token).ConfigureAwait(false);

        await WriteAsync(writer, "workbench_set_display_language", new { locale }, token).ConfigureAwait(false);
        var seen = await QueryAsync(reader, "workbench_get_display_language", null, token).ConfigureAwait(false);
        Require(string.Equals(seen.GetProperty("locale").GetString(), locale, StringComparison.Ordinal) &&
            seen.GetProperty("rightToLeft").GetBoolean() == string.Equals(locale, "ar", StringComparison.Ordinal),
            "The paired reader did not observe the selected locale.");
    }

    private static async Task VerifyDocumentAsync(McpClient writer, McpClient reader, DocumentContext original, string path, CancellationToken token)
    {
        var revision = Document(Data(await WriteAsync(writer, "editor_edit_text",
            new { document = original, offset = 5, length = 1, text = "2" }, token).ConfigureAwait(false)));
        var selection = Data(await WriteAsync(writer, "editor_find_text",
            new { document = revision, query = "move" }, token).ConfigureAwait(false));
        Require(selection.GetProperty("offset").GetInt32() == 0 && selection.GetProperty("length").GetInt32() == 4, "Find did not select source text.");
        var replacement = Data(await WriteAsync(writer, "editor_replace_text",
            new { document = Document(selection.GetProperty("document")), query = "move", replacement = "load" }, token).ConfigureAwait(false));
        Require(replacement.GetProperty("replacements").GetInt32() == 1, "Replace did not edit exactly one source match.");
        var replaced = Document(replacement.GetProperty("document"));
        var undone = Document(Data(await WriteAsync(writer, "editor_undo", new { document = replaced }, token).ConfigureAwait(false)));
        var redone = Document(Data(await WriteAsync(writer, "editor_redo", new { document = undone }, token).ConfigureAwait(false)));
        var view = await QueryAsync(reader, "editor_get_document", new { viewKey = original.ViewKey }, token).ConfigureAwait(false);
        Require(redone == Document(view) && redone.Revision > replaced.Revision && redone.DocumentId == original.DocumentId,
            "The paired reader did not see the original document's live undo and redo revision.");

        var context = await ReadContextAsync(writer, token).ConfigureAwait(false);
        await ExpectAsync(writer, "editor_save_document", new { document = original, context }, "document_changed", token).ConfigureAwait(false);
        await ExpectAsync(writer, "editor_close_document", new { document = redone, context }, "unsaved", token).ConfigureAwait(false);
        Require(string.Equals(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), Original, StringComparison.Ordinal),
            "A stale save or other buffer-only action changed the source file.");
        var saved = Document(Data(await WriteAsync(writer, "editor_save_document", new { document = redone }, token).ConfigureAwait(false)));
        Require(string.Equals(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), Expected, StringComparison.Ordinal),
            "The saved production source differs from the editor result.");
        await WriteAsync(writer, "workspace_reveal_entry", new { rootId = saved.RootId, relativePath = saved.RelativePath }, token).ConfigureAwait(false);
        var revealed = await ReadContextAsync(reader, token).ConfigureAwait(false);
        Require(string.Equals(revealed.GetProperty("activeToolId").GetString(), "explorer", StringComparison.Ordinal), "Reveal did not focus Project Explorer.");
        await WriteAsync(writer, "editor_close_document", new { document = saved }, token).ConfigureAwait(false);
        await ExpectAsync(reader, "editor_get_document", new { viewKey = original.ViewKey }, "view_closed", token).ConfigureAwait(false);
    }

    private static DocumentContext Document(JsonElement data) =>
        data.Deserialize<DocumentContext>(WireJson.Options) ?? throw new InvalidDataException("Missing production document context.");

    private static JsonElement Data(JsonElement result) => result.GetProperty("data");

    private static async Task<JsonElement> ReadContextAsync(McpClient client, CancellationToken token) =>
        (await ExpectAsync(client, "workbench_get_context", null, "ok", token).ConfigureAwait(false)).GetProperty("context");

    private static async Task<JsonElement> QueryAsync(McpClient client, string name, object? input, CancellationToken token) =>
        Data(await ExpectAsync(client, name, input, "ok", token).ConfigureAwait(false));

    private static async Task<JsonElement> WriteAsync(McpClient client, string name, object input, CancellationToken token)
    {
        var arguments = Arguments(input);
        arguments.Add("context", await ReadContextAsync(client, token).ConfigureAwait(false));
        return await CallAsync(client, name, arguments, "ok", token).ConfigureAwait(false);
    }

    private static Task<JsonElement> ExpectAsync(McpClient client, string name, object? input, string expected, CancellationToken token) =>
        CallAsync(client, name, Arguments(input), expected, token);

    private static Dictionary<string, object?> Arguments(object? input) => input is null
        ? new(StringComparer.Ordinal)
        : JsonSerializer.SerializeToElement(input, WireJson.Options).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value, StringComparer.Ordinal);

    private static async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?> arguments, string expected,
        CancellationToken token)
    {
        CallToolResult result = await client.CallToolAsync(name, arguments, cancellationToken: token).ConfigureAwait(false);
        var detail = result.StructuredContent ?? throw new InvalidDataException($"{name} did not return structured content.");
        Require(string.Equals(detail.GetProperty("code").GetString(), expected, StringComparison.Ordinal), $"{name} expected {expected}, got {detail}.");
        return detail;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
