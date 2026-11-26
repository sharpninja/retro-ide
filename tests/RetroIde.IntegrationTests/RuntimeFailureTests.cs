using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies focused recovery boundaries for unexpected runtime exceptions.</summary>
public sealed class RuntimeFailureTests
{
    /// <summary>Exercises the saved layout and workspace fallback with the real desktop log sink.</summary>
    [Fact]
    public void Corrupt_preferences_leave_a_log_and_fall_back_to_defaults()
    {
        var folder = Directory.CreateTempSubdirectory("retro-ide-invalid-state-");
        try
        {
            using var logs = LoggerFactory.Create(builder => builder.AddProvider(new DesktopErrorLog(folder.FullName)));
            var layout = Path.Combine(folder.FullName, "layout.json");
            var workspace = Path.Combine(folder.FullName, "workspace.json");
            File.WriteAllText(layout, "{");
            File.WriteAllText(workspace, "{");
            Assert.Null(new LayoutStore(layout, logs.CreateLogger<LayoutStore>()).Load());
            Assert.Null(new WorkspaceExplorerStore(workspace, logs.CreateLogger<WorkspaceExplorerStore>()).Load());
            var file = Assert.Single(Directory.GetFiles(Path.Combine(folder.FullName, "logs"), "*.log"));
            var text = File.ReadAllText(file);
            Assert.Contains("using the default layout", text, StringComparison.Ordinal);
            Assert.Contains("using an empty workspace", text, StringComparison.Ordinal);
            Assert.Contains("JsonException", text, StringComparison.Ordinal);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Checks that a failed watcher publication is logged and redelivered.</summary>
    [Fact]
    public async Task Watcher_reports_and_retries_a_failed_notification()
    {
        var folder = Directory.CreateTempSubdirectory("retro-ide-watch-recovery-");
        try
        {
            using var logs = LoggerFactory.Create(builder => builder.AddProvider(new DesktopErrorLog(folder.FullName)));
            var published = new TaskCompletionSource<WorkspaceExplorer.Address>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = 0;
            using var watch = new WorkspaceWatchSet(changes =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new InvalidOperationException("injected watcher delivery failure");
                published.TrySetResult(changes[0].Directory);
            }, logs.CreateLogger("Explorer"));
            var root = new WorkspaceExplorer.Root(Guid.NewGuid(), WorkspaceExplorer.RootKind.Folder, folder.FullName);
            watch.UpdateRoots([root]);
            Assert.Equal(new WorkspaceExplorer.Address(root.Id, ""), await published.Task
                .WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken).ConfigureAwait(true));
            var file = Assert.Single(Directory.GetFiles(Path.Combine(folder.FullName, "logs"), "*.log"));
            Assert.Contains("injected watcher delivery failure", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Checks failure isolation across successive requests to the live MCP listener.</summary>
    [Fact]
    public async Task Mcp_exception_is_correlated_and_the_next_call_succeeds()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var scope = new WorkbenchAddress(Guid.NewGuid(), Guid.NewGuid(), null, 0);
        var registry = new FeatureRegistry(new ViewContextContract(typeof(WorkbenchViewModel)));
        var host = await LoopbackMcpHost.StartAsync(registry, new FailOnceDispatcher(), scope, token).ConfigureAwait(true);
        await using var hosted = host.ConfigureAwait(true);
        var attachment = host.CreateAttachment("runtime-recovery", canMutate: false);
        using var http = attachment.CreateHttpClient();
        var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = attachment.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http), cancellationToken: token).ConfigureAwait(true);
        await using var connected = client.ConfigureAwait(true);

        var failed = await client.CallToolAsync("workbench_get_context", cancellationToken: token).ConfigureAwait(true);
        var failure = Assert.IsType<JsonElement>(failed.StructuredContent);
        Assert.True(failed.IsError);
        Assert.Equal("internal_error", failure.GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, failure.GetProperty("operationId").GetGuid());
        Assert.DoesNotContain("private exception details", failure.GetRawText(), StringComparison.Ordinal);
        var next = await client.CallToolAsync("workbench_get_context", cancellationToken: token).ConfigureAwait(true);
        Assert.False(next.IsError);
        Assert.Equal("ok", Assert.IsType<JsonElement>(next.StructuredContent).GetProperty("code").GetString());
    }

    private sealed class FailOnceDispatcher : IWorkbenchDispatcher
    {
        private int _calls;

        public Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
            ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class =>
            Interlocked.Increment(ref _calls) == 1
                ? Task.FromException<OperationResult>(new InvalidOperationException("private exception details"))
                : Task.FromResult(new OperationResult(operationId, "ok", "Recovered."));
    }
}
