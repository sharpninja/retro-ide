using System.Reflection;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02DiagnosticNavigationContractTests
{
    /// <summary>Feeds a diagnostic fixture through the workbench route and reads the rendered editor state.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-011#ac-test-save-source. Use the same source and caret assertions as the reference fixture.</remarks>
    [AvaloniaFact]
    public async Task Diagnostic_fixture_navigates_the_rendered_editor()
    {
        using var fixture = new DiagnosticSource();
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var attachment = host.CreateAttachment("diagnostic-navigation-contract", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.True(tools.Any(tool => string.Equals(tool.Name, "workspace_open_file", StringComparison.Ordinal)),
                "The live workbench did not advertise diagnostic source navigation.");
            var editor = new LiveNavigation(client, window, session, token);
            await editor.OpenProjectAsync(fixture.Root);
            await VerifyNavigationAsync(editor, fixture);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class LiveNavigation(McpClient client, Window window, WorkbenchSession session, CancellationToken token) : INavigationPort
    {
        private Guid _root;

        public async Task OpenProjectAsync(string path)
        {
            var response = await WriteAsync("workspace_open_root", new(StringComparer.Ordinal)
            {
                ["path"] = path, ["kind"] = "project",
            });
            Assert.Equal("ok", response.GetProperty("code").GetString());
            _root = response.GetProperty("data").GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, _root);
        }

        public async Task<NavigationResult> NavigateAsync(DiagnosticLocation request)
        {
            var response = await WriteAsync("workspace_open_file", new(StringComparer.Ordinal)
            {
                ["rootId"] = _root,
                ["relativePath"] = request.Path,
                ["position"] = new { line = request.Line, column = request.Column },
            });
            var code = Assert.IsType<string>(response.GetProperty("code").GetString());
            var active = Assert.IsAssignableFrom<object>(Read(session.Layout, "CurrentSource"));
            var view = Assert.IsType<Guid>(Read(active, "ViewKey"));
            if (string.Equals(code, "ok", StringComparison.Ordinal))
            {
                var scope = response.GetProperty("data");
                Assert.Equal(_root, scope.GetProperty("rootId").GetGuid());
                Assert.Equal(view, scope.GetProperty("viewKey").GetGuid());
            }

            var editor = await FindRenderedEditorAsync(Assert.IsAssignableFrom<object>(Read(active, "Buffer")));
            var textArea = Assert.IsAssignableFrom<object>(Read(editor, "TextArea"));
            var caret = Assert.IsAssignableFrom<object>(Read(textArea, "Caret"));
            return new NavigationResult(code, view, Assert.IsType<string>(Read(active, "RelativePath")),
                Assert.IsType<string>(Read(editor, "Text")), Assert.IsType<int>(Read(caret, "Line")), Assert.IsType<int>(Read(caret, "Column")));
        }

        private async Task<Control> FindRenderedEditorAsync(object buffer)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                window.UpdateLayout();
                var editor = window.GetVisualDescendants().OfType<Control>().SingleOrDefault(element =>
                    string.Equals(AutomationProperties.GetAutomationId(element), "document.source-editor", StringComparison.Ordinal) &&
                    ReferenceEquals(Read(element, "Document"), buffer));
                if (editor is not null)
                    return editor;
                await Task.Delay(25, token);
            }
        }

        private async Task<JsonElement> WriteAsync(string name, Dictionary<string, object?> args)
        {
            var current = await client.CallToolAsync("workbench_get_context", cancellationToken: token);
            Assert.NotNull(current.StructuredContent);
            args.Add("context", current.StructuredContent.Value.GetProperty("context"));
            var result = await client.CallToolAsync(name, args, cancellationToken: token);
            Assert.NotNull(result.StructuredContent);
            return result.StructuredContent.Value;
        }

        // Late binding keeps the same rendered control assertions usable against the pre-editor workbench.
        private static object? Read(object instance, string name) =>
            Assert.IsAssignableFrom<PropertyInfo>(instance.GetType().GetProperty(name)).GetValue(instance);
    }
}
