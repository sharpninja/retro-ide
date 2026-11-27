using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Mcp;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase03OperationContractTests
{
    /// <summary>Applies the mock-gated assertions to a real Git subprocess and two SDK client transports.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-015#ac-test-mcp-operation. Verifies scope, cancelled subprocess release, and completed results at the running workbench boundary.</remarks>
    [AvaloniaFact]
    public async Task Live_workbench_obeys_the_scoped_operation_contract()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var folder = Directory.CreateTempSubdirectory("retro-operation-contract-");
        try
        {
            await VerifyLiveWorkbenchAsync(folder, token).ConfigureAwait(true);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => file.IsReadOnly))
                    file.IsReadOnly = false;
            folder.Delete(recursive: true);
        }
    }

    private static async Task VerifyLiveWorkbenchAsync(DirectoryInfo folder, CancellationToken token)
    {
        var session = new WorkbenchSession(Guid.NewGuid());
        try
        {
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token)
                .ConfigureAwait(true);
            var grant = host.CreateAttachment("phase03-operation-owner", canMutate: true);
            using var client = grant.CreateHttpClient();
            await using var direct = await ConnectAsync(grant, client, token).ConfigureAwait(true);

            var tools = await direct.ListToolsAsync(cancellationToken: token).ConfigureAwait(true);
            Assert.True(tools.Any(tool => string.Equals(tool.Name, "workbench_operation_status", StringComparison.Ordinal)),
                "The live workbench does not expose workbench_operation_status.");
            await VerifyLiveOperationsAsync(folder, host, direct, grant, token).ConfigureAwait(true);
        }
        finally
        {
            // Phase 02 has a synchronous lifetime. The later workbench drains its native watchers asynchronously.
            if ((IDisposable)session is IAsyncDisposable asyncSession)
                await asyncSession.DisposeAsync().ConfigureAwait(true);
            else
                session.Dispose();
        }
    }

    private static async Task VerifyLiveOperationsAsync(DirectoryInfo folder, LoopbackMcpHost host,
        McpClient direct, AttachmentDescriptor grant, CancellationToken token)
    {
        await using var bridge = await ConnectStdioAsync(folder, grant, token).ConfigureAwait(false);
        var unrelatedGrant = host.CreateAttachment("phase03-operation-foreign", canMutate: true);
        using var unrelatedHttp = unrelatedGrant.CreateHttpClient();
        await using var unrelated = await ConnectAsync(unrelatedGrant, unrelatedHttp, token).ConfigureAwait(false);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var checkout = folder.CreateSubdirectory("checkout").FullName;
        var remote = folder.CreateSubdirectory("remote.git").FullName;
        var hooks = folder.CreateSubdirectory("empty-hooks").FullName;
        await GitAsync(remote, token, "init", "--bare", "--initial-branch=main").ConfigureAwait(false);
        await GitAsync(checkout, token, "init", "--initial-branch=main").ConfigureAwait(false);
        await GitAsync(checkout, token, "config", "--local", "core.hooksPath", hooks).ConfigureAwait(false);
        await GitAsync(checkout, token, "config", "--local", "http.proxy", "").ConfigureAwait(false);
        await GitAsync(checkout, token, "-c", "user.name=Retro IDE Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "Seed the native fetch fixture").ConfigureAwait(false);
        await GitAsync(checkout, token, "remote", "add", "origin", remote).ConfigureAwait(false);
        await GitAsync(checkout, token, "push", "origin", "main").ConfigureAwait(false);
        const string TrackingRef = "refs/remotes/origin/main";
        await GitAsync(checkout, token, "update-ref", "-d", TrackingRef).ConfigureAwait(false);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await GitAsync(checkout, token, "remote", "add", "stalled",
            FormattableString.Invariant($"http://127.0.0.1:{endpoint.Port}/fetch")).ConfigureAwait(false);

        using var port = new LiveOperationPort(direct, bridge, unrelated, checkout, listener);
        var root = await port.MutateAsync(direct, "workspace_open_root", new(StringComparer.Ordinal)
        {
            ["path"] = checkout, ["kind"] = "folder",
        }, token).ConfigureAwait(false);
        Assert.Equal("ok", Code(root));
        var selection = await port.MutateAsync(direct, "git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root.GetProperty("data").GetProperty("id").GetGuid(), ["relativePath"] = "",
        }, token).ConfigureAwait(false);
        Assert.Equal("ok", Code(selection));

        await VerifyCancelledFetchAsync(port, token).ConfigureAwait(false);
        await VerifyCompletedFetchAsync(port, token).ConfigureAwait(false);
        Assert.Equal(await GitAsync(remote, token, "rev-parse", "refs/heads/main").ConfigureAwait(false),
            await GitAsync(checkout, token, "rev-parse", TrackingRef).ConfigureAwait(false));
    }

    private static Task<McpClient> ConnectAsync(AttachmentDescriptor descriptor, HttpClient client, CancellationToken token) =>
        McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = descriptor.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, client), cancellationToken: token);

    private static Task<McpClient> ConnectStdioAsync(DirectoryInfo folder, AttachmentDescriptor descriptor, CancellationToken token)
    {
        var path = Path.Combine(folder.FullName, "attachment.json");
        descriptor.Write(path);
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = "dotnet",
            Arguments = [typeof(RetroIde.Mcp.Stdio.BridgeEntryPoint).Assembly.Location, path],
        }), cancellationToken: token);
    }

    private static async Task<string> GitAsync(string directory, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(directory, "nonexistent-fixture-config");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            Assert.True(process.ExitCode == 0, await error.ConfigureAwait(false));
            return (await output.ConfigureAwait(false)).Trim();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static string? Code(JsonElement result) => result.GetProperty("code").GetString();

    private sealed class LiveOperationPort(McpClient direct, McpClient bridge, McpClient foreign,
        string checkout, TcpListener listener) : IOperationPort, IDisposable
    {
        private TcpClient? _connection;

        public async Task<StartedOperation> StartFetchAsync(bool stalled, CancellationToken token)
        {
            var remotes = await CallAsync(direct, "git_list_remotes", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout,
            }, token).ConfigureAwait(false);
            Assert.Equal("ok", Code(remotes));
            var started = await MutateAsync(direct, "git_fetch", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout,
                ["name"] = stalled ? "stalled" : "origin",
                ["revision"] = remotes.GetProperty("data").GetProperty("revision").GetString(),
                ["background"] = true,
            }, token).ConfigureAwait(false);
            Assert.Equal("ok", Code(started));
            var handle = started.GetProperty("data");
            if (stalled)
                _connection = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);

            return new(handle.GetProperty("operationId").GetGuid(), started.GetProperty("operationId").GetGuid(),
                handle.GetProperty("tool").GetString());
        }

        public async Task<OperationStatus> StatusAsync(Guid id, Caller caller, CancellationToken token)
        {
            var result = await CallAsync(Client(caller), "workbench_operation_status", Handle(id), token).ConfigureAwait(false);
            var code = Code(result);
            if (!string.Equals(code, "ok", StringComparison.Ordinal))
                return new(code, Guid.Empty, null, false);

            var handle = result.GetProperty("data");
            return new(code, handle.GetProperty("operationId").GetGuid(), handle.GetProperty("state").GetString(),
                handle.GetProperty("canCancel").GetBoolean());
        }

        public async Task<OperationResult> ResultAsync(Guid id, Caller caller, CancellationToken token)
        {
            var result = await CallAsync(Client(caller), "workbench_operation_result", Handle(id), token).ConfigureAwait(false);
            var code = Code(result);
            if (!string.Equals(code, "ok", StringComparison.Ordinal))
                return new(code, Guid.Empty, null);

            var original = result.GetProperty("data");
            return new(code, original.GetProperty("operationId").GetGuid(), Code(original));
        }

        public async Task<string?> CancelAsync(Guid id, Caller caller, CancellationToken token) =>
            Code(await MutateAsync(Client(caller), "workbench_operation_cancel", Handle(id), token).ConfigureAwait(false));

        public async Task<bool> WasStoppedAsync(CancellationToken token)
        {
            var socket = _connection ?? throw new InvalidOperationException("The native Git process did not connect.");
            var stream = socket.GetStream();
            try
            {
                var buffer = new byte[1024];
                while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) != 0) { }
                return true;
            }
            catch (IOException error) when (error.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
            {
                return true;
            }
        }

        public void Dispose() => _connection?.Dispose();

        internal async Task<JsonElement> MutateAsync(McpClient client, string tool,
            Dictionary<string, object?> arguments, CancellationToken token)
        {
            var context = await CallAsync(client, "workbench_get_context", null, token).ConfigureAwait(false);
            arguments["context"] = context.GetProperty("context");
            return await CallAsync(client, tool, arguments, token).ConfigureAwait(false);
        }

        private McpClient Client(Caller caller) => caller switch
        {
            Caller.Owner => direct,
            Caller.OtherTransport => bridge,
            Caller.ForeignGrant => foreign,
            _ => throw new ArgumentOutOfRangeException(nameof(caller)),
        };

        private static Dictionary<string, object?> Handle(Guid id) => new(StringComparer.Ordinal) { ["operationId"] = id };

        private static async Task<JsonElement> CallAsync(McpClient client, string tool,
            Dictionary<string, object?>? arguments, CancellationToken token)
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: token).ConfigureAwait(false);
            Assert.NotNull(result.StructuredContent);
            return result.StructuredContent.Value;
        }
    }
}
