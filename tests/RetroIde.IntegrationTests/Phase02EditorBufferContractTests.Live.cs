using System.Collections;
using System.Reflection;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Model.Core;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02EditorBufferContractTests
{
    /// <summary>Runs the shared assertions through MCP, rendered editors and the real Dock host.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-011#ac-test-buffer-state. Keep the same scenario executable against the pre-editor release.</remarks>
    [AvaloniaFact]
    public async Task Workbench_retains_independent_rendered_buffer_state()
    {
        using var files = new BufferFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var session = new WorkbenchSession(Guid.NewGuid());
        var token = deadline.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var attachment = host.CreateAttachment("editor-buffer-contract", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.True(tools.Any(tool => string.Equals(tool.Name, "editor_edit_text", StringComparison.Ordinal)),
                "The live workbench did not advertise editor buffer operations.");
            var editor = new LiveBufferPort(client, window, session, token);
            await editor.OpenProjectAsync(files.Root);
            await VerifyBufferStateAsync(editor, files);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class LiveBufferPort(McpClient client, Window window, WorkbenchSession session, CancellationToken token) : IBufferPort
    {
        private sealed record Tab(string Path, object Buffer, IDockable Dockable);
        private sealed record FloatingTab(Guid Key, Window Host, IDock Owner, IDockable Anchor);
        private readonly Dictionary<Guid, Tab> _tabs = [];
        private Guid _root;
        private FloatingTab? _floating;

        public async Task OpenProjectAsync(string path)
        {
            var result = await WriteAsync("workspace_open_root", new(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["kind"] = "project",
            });
            _root = result.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, _root);
        }

        public async Task<Guid> OpenAsync(string relativePath)
        {
            var key = await OpenPathAsync(relativePath);
            var active = Assert.IsAssignableFrom<object>(Read(session.Layout, "CurrentSource"));
            Assert.Equal(key, Assert.IsType<Guid>(Read(active, "ViewKey")));
            var buffer = Assert.IsAssignableFrom<object>(Read(active, "Buffer"));
            var scope = Assert.IsType<string>(Read(active, "Scope"));
            var dockable = session.Layout.Factory.FindDockable(session.Layout.Root,
                candidate => string.Equals(candidate.Id, scope, StringComparison.Ordinal));
            Assert.NotNull(dockable);
            _tabs.Add(key, new Tab(relativePath, buffer, dockable));
            return key;
        }

        public async Task AppendAsync(Guid tab, string text)
        {
            var before = await ObserveAsync(tab);
            await EditAsync("editor_edit_text", tab, new(StringComparer.Ordinal)
            {
                ["offset"] = before.Text.Length,
                ["length"] = 0,
                ["text"] = text,
            });
        }

        public async Task SelectAsync(Guid tab, int caret, int start, int length)
        {
            var editor = await RenderedEditorAsync(tab);
            var select = Assert.IsAssignableFrom<MethodInfo>(editor.GetType().GetMethod("Select", [typeof(int), typeof(int)]));
            select.Invoke(editor, [start, length]);
            const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.IsAssignableFrom<PropertyInfo>(editor.GetType().GetProperty("CaretOffset", Access)).SetValue(editor, caret);
        }

        public Task FindAsync(Guid tab, string query) =>
            EditAsync("editor_find_text", tab, new(StringComparer.Ordinal) { ["query"] = query });

        public Task ReplaceAllAsync(Guid tab, string query, string replacement) =>
            EditAsync("editor_replace_text", tab, new(StringComparer.Ordinal)
            {
                ["query"] = query, ["replacement"] = replacement, ["all"] = true,
            });

        public async Task FloatAsync(Guid tab)
        {
            await RenderedEditorAsync(tab);
            var dockable = _tabs[tab].Dockable;
            var owner = Assert.IsAssignableFrom<IDock>(dockable.Owner);
            var anchor = Assert.IsAssignableFrom<IDockable>(owner.VisibleDockables?.FirstOrDefault(candidate => !ReferenceEquals(candidate, dockable)));
            session.Layout.Factory.FloatDockable(dockable);
            var floating = Assert.IsAssignableFrom<Window>(Assert.Single(session.Layout.Root.Windows!).Host);
            Assert.NotSame(window, floating);
            _floating = new FloatingTab(tab, floating, owner, anchor);
        }

        public Task RedockAsync(Guid tab)
        {
            var floating = Assert.IsType<FloatingTab>(_floating);
            Assert.Equal(tab, floating.Key);
            var dockable = _tabs[tab].Dockable;
            session.Layout.Factory.MoveDockable(Assert.IsAssignableFrom<IDock>(dockable.Owner), floating.Owner, dockable, floating.Anchor);
            session.Layout.Factory.SetActiveDockable(dockable);
            Assert.Same(floating.Owner, dockable.Owner);
            _floating = null;
            return Task.CompletedTask;
        }

        public Task UndoAsync(Guid tab) => EditAsync("editor_undo", tab, new(StringComparer.Ordinal));

        public Task RedoAsync(Guid tab) => EditAsync("editor_redo", tab, new(StringComparer.Ordinal));

        public async Task<BufferState> ObserveAsync(Guid tab)
        {
            var editor = await RenderedEditorAsync(tab);
            var buffer = Assert.IsAssignableFrom<object>(Read(editor, "Document"));
            var textArea = Assert.IsAssignableFrom<object>(Read(editor, "TextArea"));
            var view = Assert.IsAssignableFrom<object>(Read(textArea, "TextView"));
            var transformers = Assert.IsAssignableFrom<IEnumerable>(Read(view, "LineTransformers"));
            var highlighted = transformers.Cast<object>().Any(transformer =>
                string.Equals(transformer.GetType().Assembly.GetName().Name, "AvaloniaEdit.TextMate", StringComparison.Ordinal));
            var undo = Assert.IsAssignableFrom<object>(Read(buffer, "UndoStack"));
            return new BufferState(buffer, Assert.IsType<string>(Read(editor, "Text")), Assert.IsType<int>(Read(editor, "CaretOffset")),
                Assert.IsType<int>(Read(editor, "SelectionStart")), Assert.IsType<int>(Read(editor, "SelectionLength")),
                highlighted, ReferenceEquals(TopLevel.GetTopLevel(editor), _floating?.Host), Assert.IsType<bool>(Read(undo, "CanUndo")));
        }

        private async Task<Control> RenderedEditorAsync(Guid tab)
        {
            var entry = _tabs[tab];
            var host = _floating is { Key: var key } floating && key == tab ? floating.Host : window;
            if (ReferenceEquals(host, window) &&
                (Read(session.Layout, "CurrentSource") is not { } active || !Equals(tab, Read(active, "ViewKey"))))
            {
                Assert.Equal(tab, await OpenPathAsync(entry.Path));
            }

            while (true)
            {
                token.ThrowIfCancellationRequested();
                host.UpdateLayout();
                var editor = host.GetVisualDescendants().OfType<Control>().SingleOrDefault(element =>
                    string.Equals(AutomationProperties.GetAutomationId(element), "document.source-editor", StringComparison.Ordinal) &&
                    ReferenceEquals(Read(element, "Document"), entry.Buffer));
                if (editor is not null)
                    return editor;
                await Task.Delay(25, token);
            }
        }

        private async Task<Guid> OpenPathAsync(string path)
        {
            var document = await WriteAsync("workspace_open_file", new(StringComparer.Ordinal)
            {
                ["rootId"] = _root,
                ["relativePath"] = path,
            });
            Assert.Equal(_root, document.GetProperty("rootId").GetGuid());
            return document.GetProperty("viewKey").GetGuid();
        }

        private async Task EditAsync(string name, Guid viewKey, Dictionary<string, object?> args)
        {
            var document = await QueryAsync("editor_get_document", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["viewKey"] = viewKey,
            });
            args.Add("document", document.GetProperty("data"));
            await WriteAsync(name, args);
        }

        private async Task<JsonElement> WriteAsync(string name, Dictionary<string, object?> args)
        {
            var context = await QueryAsync("workbench_get_context");
            args.Add("context", context.GetProperty("context"));
            return (await QueryAsync(name, args)).GetProperty("data");
        }

        private async Task<JsonElement> QueryAsync(string name, IReadOnlyDictionary<string, object?>? args = null)
        {
            var response = await client.CallToolAsync(name, args, cancellationToken: token);
            Assert.NotNull(response.StructuredContent);
            var result = response.StructuredContent.Value;
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        // Use existing runtime properties without requiring AvaloniaEdit or editor ViewModels on the starting build.
        private static object? Read(object instance, string name)
        {
            const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = instance.GetType().GetProperty(name, Access);
            Assert.True(property is not null, $"Missing observable property {instance.GetType().Name}.{name}.");
            return property.GetValue(instance);
        }
    }
}
