using System.Collections;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Application;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02ExplorerPersistenceContractTests
{
    /// <summary>Drives the persisted workbench and parks a real directory result during a registration switch.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-persistence. Binds the independent assertions to live MCP and the directory handler.</remarks>
    [AvaloniaFact]
    public async Task Explorer_restores_workspace_and_rejects_a_retired_listing()
    {
        using var files = new PersistenceFiles();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var baseline = new WorkbenchSession(Guid.NewGuid());
        await using var attachment = await Attachment.StartAsync(baseline, timeout.Token);
        var tools = await attachment.Client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.True(tools.Any(tool => string.Equals(tool.Name, "workspace_list_roots", StringComparison.Ordinal)),
            "The live workbench did not advertise workspace_list_roots for Explorer persistence.");

        using var driver = new LivePersistence(files, timeout.Token);
        await VerifyPersistenceAsync(driver, files);
    }

    private sealed class LivePersistence(PersistenceFiles files, CancellationToken token) : IPersistence, IDisposable
    {
        private WorkbenchSession? _reopened;
        private Guid _folderId;

        public async Task<WorkspaceState> SeedAndCloseAsync()
        {
            using var session = OpenPersistedSession();
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await using var attachment = await Attachment.StartAsync(session, token);
                _folderId = await AddRootAsync(attachment.Client, files.Folder, "folder");
                await AddRootAsync(attachment.Client, files.Project, "project");
                await WriteAsync(attachment.Client, "workspace_select_entry", new(StringComparer.Ordinal)
                {
                    ["rootId"] = _folderId,
                    ["relativePath"] = "restore.asm",
                });
                return await CaptureAsync(session, attachment.Client);
            }
            finally
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                window.Close();
                await closed.Task.WaitAsync(token);
            }
        }

        public async Task<WorkspaceState> ReadReopenedAsync()
        {
            _reopened = OpenPersistedSession();
            await using var attachment = await Attachment.StartAsync(_reopened, token);
            return await CaptureAsync(_reopened, attachment.Client);
        }

        public async Task<SwitchResult> SwitchWhilePendingAsync()
        {
            var previous = Assert.IsType<WorkbenchSession>(_reopened);
            var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = MakeDeferredListing(previous, captured, resume);
            var actor = new ActorGrant("explorer-persistence", previous.View.WorkspaceId, previous.View.ViewKey, canMutate: false);
            var operation = Guid.NewGuid();
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["rootId"] = JsonSerializer.SerializeToElement(_folderId),
                ["relativePath"] = JsonSerializer.SerializeToElement(string.Empty),
            };
            var pending = read.InvokeAsync(previous.Controller, new ViewContextContract(typeof(WorkbenchViewModel)), arguments, actor, operation, token);
            using var current = new WorkbenchSession(Guid.NewGuid());
            JsonElement currentRoot;
            JsonElement branch;
            try
            {
                await captured.Task.WaitAsync(token);
                previous.Controller.Unregister(previous.View);
                await using var attachment = await Attachment.StartAsync(current, token);
                var rootId = await AddRootAsync(attachment.Client, files.Replacement, "folder");
                currentRoot = Assert.Single((await ReadAsync(attachment.Client, "workspace_list_roots")).EnumerateArray());
                branch = await ReadAsync(attachment.Client, "workspace_list_children", new(StringComparer.Ordinal)
                {
                    ["rootId"] = rootId,
                    ["relativePath"] = string.Empty,
                });
                Assert.Equal(current.View.WorkspaceId, branch.GetProperty("workspaceId").GetGuid());
            }
            finally { resume.TrySetResult(); }

            var stale = await pending.WaitAsync(token);
            return new SwitchResult(previous.View.WorkspaceId, current.View.WorkspaceId, operation, stale.OperationId, stale.Code,
                Read(stale, "Data") is JsonElement, currentRoot.GetProperty("path").GetString()!,
                branch.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()!).ToArray());
        }

        private WorkbenchSession OpenPersistedSession()
        {
            var type = Assert.IsAssignableFrom<Type>(Type.GetType("RetroIde.Application.Workspaces.WorkspaceExplorerStore, RetroIde.Application"));
            var store = Activator.CreateInstance(type, Access, binder: null, [files.Store], culture: null);
            return Assert.IsType<WorkbenchSession>(Activator.CreateInstance(typeof(WorkbenchSession), Access,
                binder: null, [store, null, null], culture: null));
        }

        private async Task<WorkspaceState> CaptureAsync(WorkbenchSession session, McpClient client)
        {
            var roots = await ReadAsync(client, "workspace_list_roots");
            var explorer = Assert.IsAssignableFrom<object>(Read(session, "ExplorerView"));
            var tree = Assert.IsAssignableFrom<IEnumerable>(Read(explorer, "TreeRoots"));
            object? selected;
            while ((selected = Read(explorer, "SelectedNode")) is null)
                await Task.Delay(20, token);
            var states = roots.EnumerateArray().Select(root =>
            {
                var id = root.GetProperty("id").GetGuid();
                var node = Assert.Single(tree.Cast<object>(), entry => Address(entry).RootId == id);
                return new RootState(id, root.GetProperty("kind").GetString()!.ToLowerInvariant(), root.GetProperty("path").GetString()!,
                    Assert.IsType<bool>(Read(node, "IsExpanded")));
            }).ToArray();
            return new WorkspaceState(session.View.WorkspaceId, session.View.ViewKey, states, Address(selected));
        }

        private async Task<Guid> AddRootAsync(McpClient client, string path, string kind)
        {
            var response = await WriteAsync(client, "workspace_open_root", new(StringComparer.Ordinal) { ["path"] = path, ["kind"] = kind });
            var id = response.GetProperty("data").GetProperty("id").GetGuid();
            await WriteAsync(client, "workspace_set_expanded", new(StringComparer.Ordinal)
            {
                ["rootId"] = id,
                ["relativePath"] = string.Empty,
                ["isExpanded"] = true,
            });
            return id;
        }

        private async Task<JsonElement> ReadAsync(McpClient client, string name, Dictionary<string, object?>? args = null) =>
            (await CallAsync(client, name, args)).GetProperty("data");

        private async Task<JsonElement> WriteAsync(McpClient client, string name, Dictionary<string, object?> args)
        {
            var current = await CallAsync(client, "workbench_get_context");
            args.Add("context", current.GetProperty("context"));
            return await CallAsync(client, name, args);
        }

        private async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?>? args = null)
        {
            var response = await client.CallToolAsync(name, args, cancellationToken: token);
            Assert.NotNull(response.StructuredContent);
            var result = response.StructuredContent.Value;
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result;
        }

        private static FileAddress Address(object node)
        {
            var address = Assert.IsAssignableFrom<object>(Read(node, "Address"));
            return new FileAddress(Assert.IsType<Guid>(Read(address, "RootId")), Assert.IsType<string>(Read(address, "RelativePath")));
        }

        public void Dispose() => _reopened?.Dispose();
    }

    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object? Read(object instance, string name) =>
        Assert.IsAssignableFrom<PropertyInfo>(instance.GetType().GetProperty(name, Access)).GetValue(instance);

    private static FeatureRegistration MakeDeferredListing(WorkbenchSession session, TaskCompletionSource captured, TaskCompletionSource resume)
    {
        var explorer = Assert.IsAssignableFrom<object>(Read(session.Registry, "Explorer"));
        var original = Assert.IsAssignableFrom<FeatureRegistration>(Read(explorer, "ReadChildren"));
        var prepare = Assert.IsAssignableFrom<Delegate>(Read(original, "PrepareAsync"));
        var result = prepare.Method.ReturnType.GenericTypeArguments.Single();
        var input = original.GetType().GenericTypeArguments.Single();
        var factory = Assert.IsAssignableFrom<MethodInfo>(typeof(Phase02ExplorerPersistenceContractTests).GetMethod(nameof(WrapListing),
            BindingFlags.Static | BindingFlags.NonPublic));
        return Assert.IsAssignableFrom<FeatureRegistration>(factory.MakeGenericMethod(input, result).Invoke(null, [original, prepare, captured, resume]));
    }

    private static FeatureRegistration WrapListing<TInput, TResult>(FeatureRegistration original, Delegate prepare,
        TaskCompletionSource captured, TaskCompletionSource resume) where TInput : class
    {
        var actual = Assert.IsType<Func<TInput, IWorkbenchRegistration, CancellationToken, Task<TResult>>>(prepare);
        Func<TInput, IWorkbenchRegistration, CancellationToken, Task<TResult>> delay = async (input, view, token) =>
        {
            var listing = await actual(input, view, token).ConfigureAwait(false);
            captured.SetResult();
            await resume.Task.WaitAsync(token).ConfigureAwait(false);
            return listing;
        };
        var output = Assert.IsAssignableFrom<Type>(Read(original, "DataType"));
        return Assert.IsAssignableFrom<FeatureRegistration>(Activator.CreateInstance(original.GetType(), Access, binder: null,
            [original.Id, original.ToolName, original.Description, delay, output, false], culture: null));
    }

    private sealed class Attachment(LoopbackMcpHost host, HttpClient http, McpClient client) : IAsyncDisposable
    {
        public McpClient Client => client;

        public static async Task<Attachment> StartAsync(WorkbenchSession session, CancellationToken token)
        {
            var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token).ConfigureAwait(false);
            try
            {
                var saved = host.CreateAttachment("explorer-persistence", canMutate: true);
                var http = saved.CreateHttpClient();
                try
                {
                    var client = await McpClient.CreateAsync(new HttpClientTransport(new()
                    {
                        Endpoint = saved.Endpoint,
                        TransportMode = HttpTransportMode.StreamableHttp,
                    }, http), cancellationToken: token).ConfigureAwait(false);
                    return new Attachment(host, http, client);
                }
                catch { http.Dispose(); throw; }
            }
            catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                http.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
