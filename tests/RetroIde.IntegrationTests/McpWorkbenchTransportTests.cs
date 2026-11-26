using System.Net;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises a single live workbench with independent official clients.</summary>
public sealed class McpWorkbenchTransportTests
{
    /// <summary>TEST-RETRO-015#ac-test-mcp-same-instance; TEST-RETRO-015#ac-test-mcp-viewmodel-context</summary>
    [AvaloniaFact]
    public async Task Http_stdio_and_ui_focus_the_same_live_dockable()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session);
        window.Show();
        var directory = Directory.CreateTempSubdirectory();
        using var otherSession = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, timeout.Token);
            await using var otherHost = await LoopbackMcpHost.StartAsync(otherSession.Registry, otherSession.Controller, otherSession.View.Address, timeout.Token);
            var descriptor = host.CreateAttachment("test-editor", canMutate: true);
            var otherDescriptor = otherHost.CreateAttachment("different-ide", canMutate: true);
            var path = Path.Combine(directory.FullName, "attach.json");
            descriptor.Write(path);
            AssertOwnerOnlyDescriptor(path);
            using var http = descriptor.CreateHttpClient();
            using var wrongInstance = await http.GetAsync(otherDescriptor.Endpoint, timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongInstance.StatusCode);
            await using var direct = await ConnectHttp(descriptor.Endpoint, http, timeout.Token);
            await using var bridge = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = "dotnet",
                Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, path],
            }), cancellationToken: timeout.Token);

            var directTools = await direct.ListToolsAsync(cancellationToken: timeout.Token);
            var bridgeTools = await bridge.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Equal(session.Registry.Features.Select(feature => feature.ToolName).Order(StringComparer.Ordinal),
                directTools.Select(tool => tool.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
            var focusTool = Assert.Single(directTools, tool => string.Equals(tool.Name, "workbench_focus_tool", StringComparison.Ordinal));
            Assert.Equal(session.Registry.Context.Schema.GetRawText(),
                focusTool.JsonSchema.GetProperty("properties").GetProperty("context").GetRawText());
            Assert.Equal(directTools.Select(x => x.Name), bridgeTools.Select(x => x.Name), StringComparer.Ordinal);
            Assert.Equal(directTools.Select(x => x.JsonSchema.GetRawText()), bridgeTools.Select(x => x.JsonSchema.GetRawText()), StringComparer.Ordinal);
            Assert.Equal(directTools.Select(x => x.ProtocolTool.OutputSchema!.Value.GetRawText()),
                bridgeTools.Select(x => x.ProtocolTool.OutputSchema!.Value.GetRawText()), StringComparer.Ordinal);
            var initial = await Call(direct, "workbench_get_context", null, timeout.Token);
            var bridged = await Call(bridge, "workbench_get_context", null, timeout.Token);
            Assert.Equal(Context(initial).GetRawText(), Context(bridged).GetRawText());
            Assert.Equal(session.View.ViewKey, Context(initial).GetProperty("viewKey").GetGuid());

            session.Layout.Focus("output");
            var uiBefore = session.View.WorkbenchRevision;
            session.View.FocusTarget = "registers";
            await WorkbenchRemoteControl.ClickAsync(window, "workbench.view.focus.toolbar");
            await Assert.IsAssignableFrom<Task>(session.View.FocusCommand.ExecutionTask);
            Assert.Equal(uiBefore + 1, session.View.WorkbenchRevision);
            Assert.True(session.Layout.IsFocused("registers"));
            session.Layout.Focus("output");
            var directContext = Context(await Call(direct, "workbench_get_context", null, timeout.Token));
            var first = await Call(direct, "workbench_focus_tool", Focus(directContext, "registers"), timeout.Token);
            Assert.Equal("ok", Code(first));
            Assert.Equal(directContext.GetProperty("workbenchRevision").GetInt64() + 1, session.View.WorkbenchRevision);
            Assert.True(session.Layout.IsFocused("registers"));
            Assert.Equal(session.View.LastResult?.Context?.GetProperty("activeToolId").GetString(), Context(first).GetProperty("activeToolId").GetString());

            session.Layout.Focus("output");
            var bridgeContext = Context(await Call(bridge, "workbench_get_context", null, timeout.Token));
            var second = await Call(bridge, "workbench_focus_tool", Focus(bridgeContext, "registers"), timeout.Token);
            Assert.Equal("ok", Code(second));
            Assert.Equal(bridgeContext.GetProperty("workbenchRevision").GetInt64() + 1, session.View.WorkbenchRevision);
            Assert.True(session.Layout.IsFocused("registers"));
            Assert.Equal(session.View.WorkbenchRevision, Context(second).GetProperty("workbenchRevision").GetInt64());
            Assert.Equal(7, session.Controller.DispatchCount);
            Assert.Equal(0, otherSession.Controller.DispatchCount);
            Assert.Equal(0, otherSession.View.WorkbenchRevision);

            var stale = await Call(bridge, "workbench_focus_tool", Focus(bridgeContext, "output"), timeout.Token);
            Assert.Equal("stale_revision", Code(stale));
            Assert.True(session.Layout.IsFocused("registers"));
            var lastLive = Context(await Call(bridge, "workbench_get_context", null, timeout.Token));
            session.Controller.Unregister(session.View);
            var closed = await Call(bridge, "workbench_focus_tool", Focus(lastLive, "output"), timeout.Token);
            Assert.Equal("view_closed", Code(closed));
            Assert.True(session.Layout.IsFocused("registers"));
        }
        finally
        {
            window.Close();
            directory.Delete(recursive: true);
        }
    }

    /// <summary>TEST-RETRO-015#ac-test-mcp-scope; TEST-RETRO-015#ac-test-mcp-viewmodel-context</summary>
    [AvaloniaFact]
    public async Task Transport_and_live_scope_rejections_have_no_focus_effect()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, timeout.Token);
        var editor = host.CreateAttachment("test-editor", canMutate: true);
        var observer = host.CreateAttachment("test-observer", canMutate: false);
        using var http = editor.CreateHttpClient();
        using var observerHttp = observer.CreateHttpClient();
        observerHttp.DefaultRequestHeaders.Add("Origin", observer.Endpoint.GetLeftPart(UriPartial.Authority));
        using var anonymous = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var anonymousResult = await anonymous.GetAsync(editor.Endpoint, timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResult.StatusCode);
        using var badHost = new HttpRequestMessage(HttpMethod.Get, editor.Endpoint);
        badHost.Headers.Host = "example.invalid";
        using var badHostResult = await http.SendAsync(badHost, timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, badHostResult.StatusCode);

        await using var client = await ConnectHttp(editor.Endpoint, http, timeout.Token);
        await using var reader = await ConnectHttp(observer.Endpoint, observerHttp, timeout.Token);
        var live = Context(await Call(reader, "workbench_get_context", null, timeout.Token));
        using var badOrigin = new HttpRequestMessage(HttpMethod.Post, editor.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = "workbench_focus_tool", arguments = Focus(live, "registers") },
            }),
        };
        badOrigin.Headers.Add("Origin", "https://example.invalid");
        badOrigin.Headers.Add("Accept", "application/json, text/event-stream");
        using var rejected = await http.SendAsync(badOrigin, timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(0, session.View.WorkbenchRevision);
        Assert.Equal("forbidden", Code(await Call(reader, "workbench_focus_tool", Focus(live, "registers"), timeout.Token)));
        var foreign = JsonNode.Parse(live.GetRawText())!.AsObject();
        foreign["workspaceId"] = Guid.NewGuid();
        Assert.Equal("workspace_mismatch", Code(await Call(client, "workbench_focus_tool", Focus(JsonSerializer.SerializeToElement(foreign), "registers"), timeout.Token)));
        var otherView = new WorkbenchViewModel(session.View.WorkspaceId);
        session.Controller.Register(otherView);
        Assert.Same(otherView, session.Controller.GetView<IView>(otherView.ViewKey));
        var wrongView = JsonNode.Parse(live.GetRawText())!.AsObject();
        wrongView["viewKey"] = otherView.ViewKey;
        Assert.Equal("view_mismatch", Code(await Call(client, "workbench_focus_tool",
            Focus(JsonSerializer.SerializeToElement(wrongView), "registers"), timeout.Token)));
        var hidden = JsonNode.Parse(live.GetRawText())!.AsObject();
        hidden["command"] = "workbench.view.focus";
        Assert.Equal("invalid_arguments", Code(await Call(client, "workbench_focus_tool", Focus(JsonSerializer.SerializeToElement(hidden), "registers"), timeout.Token)));
        Assert.Equal("tool_unavailable", Code(await Call(client, "workbench_focus_tool", Focus(live, "unknown"), timeout.Token)));
        Assert.Equal(0, session.View.WorkbenchRevision);

        var focused = await Call(client, "workbench_focus_tool", Focus(live, "registers"), timeout.Token);
        Assert.Equal("ok", Code(focused));
        var beforeClose = Context(focused);
        var forgedSelection = JsonNode.Parse(beforeClose.GetRawText())!.AsObject();
        forgedSelection["activeToolId"] = "output";
        Assert.Equal("context_mismatch", Code(await Call(client, "workbench_focus_tool",
            Focus(JsonSerializer.SerializeToElement(forgedSelection), "output"), timeout.Token)));
        Assert.True(session.Layout.IsFocused("registers"));
        var stale = JsonNode.Parse(beforeClose.GetRawText())!.AsObject();
        stale["workbenchRevision"] = live.GetProperty("workbenchRevision").GetInt64();
        Assert.Equal("stale_revision", Code(await Call(client, "workbench_focus_tool",
            Focus(JsonSerializer.SerializeToElement(stale), "output"), timeout.Token)));
        Assert.True(session.Layout.IsFocused("registers"));
        session.Controller.Unregister(session.View);
        Assert.Equal("view_closed", Code(await Call(client, "workbench_focus_tool", Focus(beforeClose, "output"), timeout.Token)));
        Assert.True(session.Layout.IsFocused("registers"));
        Assert.Equal(1, session.View.WorkbenchRevision);
        host.Revoke(observer);
        using var revoked = await observerHttp.GetAsync(editor.Endpoint, timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    /// <summary>TEST-RETRO-015#ac-test-mcp-scope</summary>
    [AvaloniaFact]
    public async Task Revoking_an_authenticated_HTTP_mutation_while_queued_prevents_focus()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        Assert.True(session.Layout.Focus("registers"));
        var dispatcher = new QueuedMutation(session.Controller);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, dispatcher, session.View.Address, timeout.Token);
        var attachment = host.CreateAttachment("queued-editor", canMutate: true);
        using var http = attachment.CreateHttpClient();
        await using var client = await ConnectHttp(attachment.Endpoint, http, timeout.Token);
        var revision = session.View.WorkbenchRevision;
        var address = JsonSerializer.SerializeToElement(session.View.Address, WireJson.Options);
        var pending = Call(client, "workbench_focus_tool", Focus(address, "output"), timeout.Token).AsTask();
        try
        {
            var operationId = await dispatcher.Entered.Task.WaitAsync(timeout.Token);
            Assert.False(pending.IsCompleted);
            host.Revoke(attachment);
            dispatcher.Release.TrySetResult();
            var result = await pending;
            Assert.Equal("unauthorized", Code(result));
            Assert.Equal(operationId, result.StructuredContent!.Value.GetProperty("operationId").GetGuid());
            Assert.Equal(revision, session.View.WorkbenchRevision);
            Assert.True(session.Layout.IsFocused("registers"));
        }
        finally { dispatcher.Release.TrySetResult(); }
    }

    /// <summary>TEST-RETRO-015#ac-test-mcp-scope</summary>
    [AvaloniaFact]
    public async Task Disconnecting_an_authenticated_HTTP_mutation_while_queued_prevents_focus()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        Assert.True(session.Layout.Focus("registers"));
        var dispatcher = new QueuedMutation(session.Controller);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = new CancellationTokenSource();
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, dispatcher, session.View.Address, timeout.Token);
        var attachment = host.CreateAttachment("disconnect-editor", canMutate: true);
        using var transport = attachment.CreateHttpClient();
        await using var client = await ConnectHttp(attachment.Endpoint, transport, timeout.Token);
        var before = session.View.Address;
        var context = JsonSerializer.SerializeToElement(before, WireJson.Options);
        var pending = Call(client, "workbench_focus_tool", Focus(context, "output"), cancellation.Token).AsTask();
        try
        {
            var operation = await dispatcher.Entered.Task.WaitAsync(timeout.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await dispatcher.Cancelled.Task.WaitAsync(timeout.Token);
            dispatcher.Release.TrySetResult();
            var result = await dispatcher.Completed.Task.WaitAsync(timeout.Token);
            Assert.Equal(operation, result.OperationId);
            Assert.Equal("cancelled", result.Code);
            Assert.Equal(before, session.View.Address);
            Assert.True(session.Layout.IsFocused("registers"));
        }
        finally { dispatcher.Release.TrySetResult(); }
    }

    private sealed class QueuedMutation(WorkbenchController controller) : IWorkbenchDispatcher
    {
        public TaskCompletionSource<Guid> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperationResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
            ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class
        {
            using var cancellation = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.SetResult(operationId);
            await Release.Task.ConfigureAwait(false);
            var result = await controller.DispatchAsync(feature, input, context, actor, operationId, cancellationToken).ConfigureAwait(false);
            Completed.TrySetResult(result);
            return result;
        }
    }

    /// <summary>Open a folder from the Explorer and a project through MCP in the same workbench.</summary>
    [AvaloniaFact]
    public async Task Root_commands_are_shared_between_explorer_and_both_transports()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var folder = Directory.CreateTempSubdirectory("retro-ide-roots-");
        var plain = Directory.CreateDirectory(Path.Combine(folder.FullName, "files")).FullName;
        var project = Directory.CreateDirectory(Path.Combine(folder.FullName, "project")).FullName;
        File.WriteAllText(Path.Combine(plain, "main.asm"), "folder");
        File.WriteAllText(Path.Combine(project, "main.asm"), "project");
        var window = new WorkbenchWindow(session);
        window.Show();
        session.ExplorerView.PickDirectoryAsync = _ => Task.FromResult<string?>(plain);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            Assert.True(session.Layout.Focus("explorer"));
            while (!window.GetVisualDescendants().OfType<Button>().Any(button =>
                string.Equals(AutomationProperties.GetAutomationId(button), "workspace.open-folder.toolbar", StringComparison.Ordinal)))
            {
                window.UpdateLayout();
                await Task.Delay(30, timeout.Token);
            }
            await WorkbenchRemoteControl.ClickAsync(window, "workspace.open-folder.toolbar");
            await Assert.IsAssignableFrom<Task>(session.ExplorerView.OpenFolderCommand.ExecutionTask);
            var folderRoot = Assert.Single(session.ExplorerView.Roots);
            Assert.Equal(WorkspaceExplorer.RootKind.Folder, folderRoot.Kind);
            Assert.Equal("ok", session.ExplorerView.LastResult?.Code);
            Assert.Null(session.View.LastResult);
            await ExpandFirstRootAsync(window, session, folderRoot, timeout.Token);

            await VerifyExplorerMcpAsync(session, folder.FullName, project, folderRoot.Id, timeout.Token);
        }
        finally
        {
            window.Close();
            folder.Delete(recursive: true);
        }
    }

    private static async Task ExpandFirstRootAsync(WorkbenchWindow window, WorkbenchSession session, WorkspaceExplorer.Root root, CancellationToken token)
    {
        var node = Assert.Single(session.ExplorerView.TreeRoots);
        Assert.Null(Assert.Single(node.Children).Address);
        TreeViewItem? item;
        do
        {
            window.UpdateLayout();
            item = window.GetVisualDescendants().OfType<TreeViewItem>().SingleOrDefault(candidate => ReferenceEquals(candidate.DataContext, node));
            if (item is null)
                await Task.Delay(30, token);
        } while (item is null);

        item.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
        while (!node.IsExpanded || node.Children.Count == 0 || node.Children[0].Address is null)
            await Task.Delay(30, token);
        var child = Assert.Single(node.Children);
        Assert.Equal("main.asm", child.Label);
        Assert.Equal(new WorkspaceExplorer.Address(root.Id, "main.asm"), child.Address);
        Assert.Contains(new WorkspaceExplorer.Address(root.Id, ""), session.Explorer.Snapshot().Expanded);

        var tree = window.GetVisualDescendants().OfType<TreeView>().Single(control =>
            ReferenceEquals(control.ItemsSource, session.ExplorerView.TreeRoots));
        tree.SetCurrentValue(TreeView.SelectedItemProperty, child);
        while (!ReferenceEquals(session.ExplorerView.SelectedNode, child))
            await Task.Delay(30, token);
        Assert.Equal(child.Address, session.Explorer.Snapshot().Selection);
    }

    private static async Task VerifyExplorerMcpAsync(WorkbenchSession session, string directory, string project, Guid folderId, CancellationToken token)
    {
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
        var descriptor = host.CreateAttachment("root-editor", canMutate: true);
        using var clientHttp = descriptor.CreateHttpClient();
        await using var client = await ConnectHttp(descriptor.Endpoint, clientHttp, token);
        var descriptorPath = Path.Combine(directory, "attach.json");
        descriptor.Write(descriptorPath);
        await using var bridge = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = "dotnet",
            Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, descriptorPath],
        }), cancellationToken: token);

        var context = Context(await Call(client, "workbench_get_context", null, token));
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = project, ["kind"] = "project", ["context"] = context,
        };
        var result = await Call(client, "workspace_open_root", args, token);
        Assert.Equal("ok", Code(result));
        var data = result.StructuredContent!.Value.GetProperty("data");
        Assert.Equal("Project", data.GetProperty("kind").GetString());
        var projectRoot = Assert.Single(session.ExplorerView.Roots, root => root.Kind == WorkspaceExplorer.RootKind.Project);
        Assert.Equal(projectRoot.Id, data.GetProperty("id").GetGuid());
        Assert.Equal("stale_revision", Code(await Call(bridge, "workspace_open_root", args, token)));

        var roots = await Call(bridge, "workspace_list_roots", null, token);
        Assert.Equal("ok", Code(roots));
        var entries = roots.StructuredContent!.Value.GetProperty("data").EnumerateArray();
        Assert.Equal([folderId, projectRoot.Id], entries.Select(root => root.GetProperty("id").GetGuid()));
        Assert.Equal(2, session.ExplorerView.Roots.Count);
        var read = new Dictionary<string, object?>(StringComparer.Ordinal) { ["rootId"] = projectRoot.Id, ["relativePath"] = "" };
        var httpBranch = await Call(client, "workspace_list_children", read, token);
        var stdioBranch = await Call(bridge, "workspace_list_children", read, token);
        var branch = httpBranch.StructuredContent!.Value.GetProperty("data");
        Assert.Equal(branch.GetRawText(), stdioBranch.StructuredContent!.Value.GetProperty("data").GetRawText());
        var file = Assert.Single(branch.GetProperty("entries").EnumerateArray());
        Assert.Equal("main.asm", file.GetProperty("name").GetString());
        Assert.Equal(projectRoot.Id, file.GetProperty("address").GetProperty("rootId").GetGuid());
    }

    private static void AssertOwnerOnlyDescriptor(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileInfo(path).GetAccessControl();
            var rule = Assert.IsType<FileSystemAccessRule>(Assert.Single(
                security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).OfType<AuthorizationRule>()));
            Assert.True(security.AreAccessRulesProtected);
            Assert.False(rule.IsInherited);
            Assert.Equal(identity.User, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    private static Task<McpClient> ConnectHttp(Uri endpoint, HttpClient http, CancellationToken token) =>
        McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp }, http), cancellationToken: token);

    private static ValueTask<CallToolResult> Call(McpClient client, string name, IReadOnlyDictionary<string, object?>? args, CancellationToken token) =>
        client.CallToolAsync(name, args, cancellationToken: token);

    private static Dictionary<string, object?> Focus(JsonElement context, string tool) =>
        new(StringComparer.Ordinal) { ["context"] = context, ["toolId"] = tool };

    private static JsonElement Context(CallToolResult result) => result.StructuredContent!.Value.GetProperty("context");
    private static string? Code(CallToolResult result) => result.StructuredContent!.Value.GetProperty("code").GetString();
}
