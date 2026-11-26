using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02ExplorerGestureContractTests
{
    /// <summary>Uses the rendered tree, scoped workbench and Reveal command with application-only input.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots. Run the same identity assertions used by the reference model.</remarks>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots. Check the live MCP route, then wait for rows to render after the roots load.</remarks>
    [AvaloniaFact]
    public async Task Explorer_tree_gestures_and_reveal_preserve_source_identity()
    {
        using var files = new GestureFiles();
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session) { Width = 1280, Height = 900 };
        window.Show();
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
            var descriptor = host.CreateAttachment("explorer-gesture-contract", canMutate: true);
            using var http = descriptor.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = descriptor.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.True(tools.Any(tool => string.Equals(tool.Name, "workspace_open_root", StringComparison.Ordinal)),
                "The live workbench did not advertise workspace_open_root for Explorer source gestures.");
            await VerifyGesturesAsync(new LiveGestures(client, window, session, token), files);
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsExplorerTree(TreeView tree) =>
        string.Equals(AutomationProperties.GetAutomationId(tree), "workspace.roots", StringComparison.Ordinal);

    private sealed class LiveGestures(McpClient client, Window window, WorkbenchSession session, CancellationToken token) : IGestures
    {
        private object Explorer => Assert.IsAssignableFrom<object>(Read(session, "ExplorerView"));

        public async Task<Guid> AddRootAsync(string path, string kind)
        {
            var response = await WriteAsync("workspace_open_root", new(StringComparer.Ordinal) { ["path"] = path, ["kind"] = kind });
            var root = response.GetProperty("data").GetProperty("id").GetGuid();
            await WriteAsync("workspace_set_expanded", new(StringComparer.Ordinal)
            {
                ["rootId"] = root,
                ["relativePath"] = "",
                ["isExpanded"] = true,
            });
            return root;
        }

        public async Task<OpenedSource> ActivateAsync(SelectedFile file, Activation activation)
        {
            Assert.True(session.Layout.Focus("explorer"));
            var row = await FindRowAsync(file);
            var open = Assert.IsAssignableFrom<object>(Read(Explorer, "OpenEntryCommand"));
            var prior = Read(open, "ExecutionTask");
            if (activation == Activation.Keyboard)
            {
                await SelectAsync(file);
                Assert.True(row.Focus());
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            }
            else
            {
                await DoubleClickAsync(row);
            }
            await AwaitCommandAsync(open, prior);
            var active = ActiveDocument();
            var buffer = Assert.IsAssignableFrom<object>(Read(active, "Buffer"));
            return new OpenedSource(Assert.IsType<Guid>(Read(active, "ViewKey")),
                ReadFile(Assert.IsAssignableFrom<object>(Read(active, "Address"))),
                Assert.IsType<string>(Read(buffer, "Text")));
        }

        public async Task SelectAsync(SelectedFile file)
        {
            await WriteAsync("workspace_select_entry", new(StringComparer.Ordinal)
            {
                ["rootId"] = file.Root,
                ["relativePath"] = file.Path,
            });
            while (TryReadSelected() != file)
                await Task.Delay(20, token);
        }

        public Task<SelectedFile> SelectedAsync() => Task.FromResult(Assert.IsType<SelectedFile>(TryReadSelected()));

        public async Task<SelectedFile> RevealAsync(Guid tab)
        {
            var active = ActiveDocument();
            Assert.Equal(tab, Assert.IsType<Guid>(Read(active, "ViewKey")));
            var reveal = Assert.IsAssignableFrom<object>(Read(active, "RevealCommand"));
            var before = Read(reveal, "ExecutionTask");
            await WorkbenchRemoteControl.ClickAsync(window, "document.reveal.toolbar");
            await AwaitCommandAsync(reveal, before);
            Assert.True(session.Layout.IsFocused("explorer"));
            return await SelectedAsync();
        }

        private object ActiveDocument() => Assert.IsAssignableFrom<object>(Read(session.Layout, "CurrentSource"));

        private SelectedFile? TryReadSelected()
        {
            var selected = Read(Explorer, "SelectedNode");
            return selected is not null && Read(selected, "Address") is { } address ? ReadFile(address) : null;
        }

        private async Task<TreeViewItem> FindRowAsync(SelectedFile expected)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<TreeView>().Where(tree => IsExplorerTree(tree) ||
                        ReferenceEquals(tree.ItemsSource, Read(Explorer, "TreeRoots")))
                    .SelectMany(tree => tree.GetVisualDescendants().OfType<TreeViewItem>())
                    .SingleOrDefault(item => item.DataContext is { } node && Read(node, "Address") is { } address && ReadFile(address) == expected);
                if (row is not null)
                    return row;
                await Task.Delay(20, token);
            }
        }

        private async Task DoubleClickAsync(TreeViewItem row)
        {
            // Let a previously completed pointer gesture expire without depending on a machine-specific interval.
            const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var setting = Assert.IsAssignableFrom<PropertyInfo>(typeof(TopLevel).GetProperty("PlatformSettings", Access));
            var platform = Assert.IsAssignableFrom<Avalonia.Platform.IPlatformSettings>(setting.GetValue(window));
            await Task.Delay(platform.GetDoubleTapTime(PointerType.Mouse) + TimeSpan.FromMilliseconds(30), token);
            row.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var node = Assert.IsAssignableFrom<object>(row.DataContext);
            var title = Assert.IsType<string>(Read(node, "Label"));
            var text = row.GetVisualDescendants().OfType<TextBlock>().Single(label => string.Equals(label.Text, title, StringComparison.Ordinal));
            var point = Assert.IsType<Point>(text.TranslatePoint(new Point(Math.Min(8, text.Bounds.Width / 2), text.Bounds.Height / 2), window));
            var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
            Assert.Same(row, hit as TreeViewItem ?? hit.FindAncestorOfType<TreeViewItem>());
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }

        private async Task AwaitCommandAsync(object command, object? previous)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (Read(command, "ExecutionTask") is Task task && !ReferenceEquals(task, previous))
                {
                    await task.WaitAsync(token);
                    return;
                }
                await Task.Delay(20, token);
            }
        }

        private async Task<JsonElement> WriteAsync(string name, Dictionary<string, object?> args)
        {
            var current = await client.CallToolAsync("workbench_get_context", cancellationToken: token);
            Assert.NotNull(current.StructuredContent);
            args.Add("context", current.StructuredContent.Value.GetProperty("context"));
            var response = await client.CallToolAsync(name, args, cancellationToken: token);
            Assert.NotNull(response.StructuredContent);
            var result = response.StructuredContent.Value;
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        private static SelectedFile ReadFile(object address) =>
            new(Assert.IsType<Guid>(Read(address, "RootId")), Assert.IsType<string>(Read(address, "RelativePath")));

        private static object? Read(object instance, string name)
        {
            const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = instance.GetType().GetProperty(name, Access);
            Assert.True(property is not null, $"Missing observable property {instance.GetType().Name}.{name}.");
            return property.GetValue(instance);
        }
    }
}
