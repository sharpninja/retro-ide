using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RetroIde.Application;
using RetroIde.Mcp;
using RetroIde.Mcp.Stdio;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class ProductionAttachmentTests
{
    private static readonly string Dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    /// <summary>
    /// TEST-RETRO-001#verify-ac-behavior-desktop-2
    /// TEST-RETRO-015#ac-test-mcp-same-instance
    /// TEST-RETRO-015#ac-test-mcp-scope
    /// TEST-RETRO-015#ac-test-mcp-viewmodel-context
    /// </summary>
    internal static async Task RunAsync(bool useNativeShortcut)
    {
        var root = Directory.CreateTempSubdirectory("retro-ide-desktops-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var token = timeout.Token;
        try
        {
            using var input = useNativeShortcut ? new NativeInput() : null;
            await using var other = new Desktop(Path.Combine(root.FullName, "other"), remoteControl: input is null);
            var otherPath = await other.WaitForAttachmentAsync(token).ConfigureAwait(false);
            await using var selected = new Desktop(Path.Combine(root.FullName, "selected"), remoteControl: input is null);
            var selectedPath = await selected.WaitForAttachmentAsync(token).ConfigureAwait(false);
            var otherDescriptor = AttachmentDescriptor.Read(otherPath);
            var selectedDescriptor = AttachmentDescriptor.Read(selectedPath);
            Require(selected.Process.Id != other.Process.Id && selectedDescriptor.Endpoint != otherDescriptor.Endpoint,
                "Expected distinct desktop processes and attachment endpoints.");

            using var otherHttp = otherDescriptor.CreateHttpClient();
            using var selectedHttp = selectedDescriptor.CreateHttpClient();
            await using var otherClient = await ConnectAsync(otherDescriptor, otherHttp, token).ConfigureAwait(false);
            await using var direct = await ConnectAsync(selectedDescriptor, selectedHttp, token).ConfigureAwait(false);
            await using var bridge = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = Dotnet,
                Arguments = [typeof(BridgeEntryPoint).Assembly.Location, selectedPath],
            }), cancellationToken: token).ConfigureAwait(false);

            var untouched = await ReadContextAsync(otherClient, token).ConfigureAwait(false);
            var initial = await ReadContextAsync(direct, token).ConfigureAwait(false);
            Require(initial == await ReadContextAsync(bridge, token).ConfigureAwait(false) &&
                initial.ViewKey != untouched.ViewKey && initial.WorkspaceId != untouched.WorkspaceId,
                "Both SDK clients must resolve the selected live context independently of the other desktop.");
            Console.WriteLine($"Production desktops selected PID={selected.Process.Id}, {initial}; other PID={other.Process.Id}, {untouched}.");

            using var wrongEndpoint = await selectedHttp.GetAsync(otherDescriptor.Endpoint, token).ConfigureAwait(false);
            Require(wrongEndpoint.StatusCode == HttpStatusCode.Unauthorized, "The other desktop accepted the selected desktop's bearer token.");
            foreach (var client in new[] { direct, bridge })
            {
                var wrong = await CallFocusAsync(client, untouched, "registers", token).ConfigureAwait(false);
                Require(Code(wrong) == "workspace_mismatch", "A selected client accepted the other desktop's context.");
            }

            var starting = await FocusAsync(direct, initial, "output", token).ConfigureAwait(false);
            await selected.FocusThroughUiAsync(input, token).ConfigureAwait(false);
            WorkbenchAddress fromUi;
            do
            {
                fromUi = await ReadContextAsync(bridge, token).ConfigureAwait(false);
                if (fromUi.WorkbenchRevision == starting.WorkbenchRevision)
                    await Task.Delay(50, token).ConfigureAwait(false);
            } while (fromUi.WorkbenchRevision == starting.WorkbenchRevision);
            CheckFocus(starting, fromUi, "registers");
            var uiRoute = useNativeShortcut ? "The native shortcut" : "RemoteControl’s production toolbar";
            Console.WriteLine($"{uiRoute} focused Registers at revision {fromUi.WorkbenchRevision}.");

            var before = await FocusAsync(bridge, fromUi, "output", token).ConfigureAwait(false);
            var fromHttp = await FocusAsync(direct, before, "registers", token).ConfigureAwait(false);
            Require(fromHttp == await ReadContextAsync(bridge, token).ConfigureAwait(false), "Stdio could not see the change made through HTTP.");
            before = await FocusAsync(direct, fromHttp, "output", token).ConfigureAwait(false);
            var fromStdio = await FocusAsync(bridge, before, "registers", token).ConfigureAwait(false);
            Require(fromStdio == await ReadContextAsync(direct, token).ConfigureAwait(false), "HTTP could not see the change made through stdio.");
            Require(untouched == await ReadContextAsync(otherClient, token).ConfigureAwait(false), "Focus changed in the unselected desktop.");
            Console.WriteLine($"HTTP and stdio focused Registers at revisions {fromHttp.WorkbenchRevision}, {fromStdio.WorkbenchRevision}.");
            Console.WriteLine("Wrong endpoint returned HTTP 401. Both selected clients rejected the other workspace and its context stayed unchanged.");

            await Phase02AttachmentTests.RunAsync(direct, bridge, otherClient, root.FullName, token).ConfigureAwait(false);
            var last = await ReadContextAsync(direct, token).ConfigureAwait(false);
            var beforeClose = await FocusAsync(direct, last, "registers", token).ConfigureAwait(false);

            await selected.CloseAsync(input, token).ConfigureAwait(false);
            await selected.WaitForExitAsync(token).ConfigureAwait(false);
            Require(!File.Exists(selectedPath), "The selected descriptor survived a graceful desktop shutdown.");
            var persisted = new LayoutStore(Path.Combine(selected.Folder, "layout.json")).Load();
            Require(persisted?.ActiveTool == "registers", "The production close did not persist Registers as the active tool.");
            await RequireUnavailableAsync(direct, beforeClose, "HTTP", token).ConfigureAwait(false);
            await RequireUnavailableAsync(bridge, beforeClose, "stdio", token).ConfigureAwait(false);
            Require(!other.Process.HasExited && untouched == await ReadContextAsync(otherClient, token).ConfigureAwait(false),
                "Closing the selected desktop affected the other desktop.");
            await other.CloseAsync(input, token).ConfigureAwait(false);
            await other.WaitForExitAsync(token).ConfigureAwait(false);
            Require(!File.Exists(otherPath), "The other descriptor survived a graceful desktop shutdown.");
            Console.WriteLine("Both live clients lost access after the selected desktop closed. Both desktops exited normally and removed their descriptors.");

            await VerifyProcessRestartAsync(selected.Folder, beforeClose, input, token).ConfigureAwait(false);
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task VerifyProcessRestartAsync(string folder, WorkbenchAddress previous, NativeInput? input, CancellationToken token)
    {
        await using var reopened = new Desktop(folder, remoteControl: input is null);
        var path = await reopened.WaitForAttachmentAsync(token).ConfigureAwait(false);
        var descriptor = AttachmentDescriptor.Read(path);
        using var http = descriptor.CreateHttpClient();
        await using var client = await ConnectAsync(descriptor, http, token).ConfigureAwait(false);
        var restored = await ReadContextAsync(client, token).ConfigureAwait(false);
        Require(restored.ActiveToolId == "registers" && restored.ViewKey != previous.ViewKey && restored.WorkspaceId == previous.WorkspaceId,
            "The production process restart did not restore Registers and the workspace with a fresh view registration.");
        await Phase02AttachmentTests.VerifyReopenAsync(client, token).ConfigureAwait(false);
        await reopened.CloseAsync(input, token).ConfigureAwait(false);
        await reopened.WaitForExitAsync(token).ConfigureAwait(false);
        Require(!File.Exists(path), "The restarted desktop did not remove its attachment descriptor.");
        Console.WriteLine("A fresh production process restored Registers and the workspace ID, then published a new ViewModel registration.");
    }

    private static Task<McpClient> ConnectAsync(AttachmentDescriptor descriptor, HttpClient http, CancellationToken token) =>
        McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = descriptor.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
        }, http), cancellationToken: token);

    private static async Task<WorkbenchAddress> ReadContextAsync(McpClient client, CancellationToken token) =>
        Context(await client.CallToolAsync("workbench_get_context", cancellationToken: token).ConfigureAwait(false));

    private static ValueTask<CallToolResult> CallFocusAsync(McpClient client, WorkbenchAddress address, string tool, CancellationToken token) =>
        client.CallToolAsync("workbench_focus_tool", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["context"] = JsonSerializer.SerializeToElement(new
            {
                viewKey = address.ViewKey,
                workspaceId = address.WorkspaceId,
                activeToolId = address.ActiveToolId,
                workbenchRevision = address.WorkbenchRevision,
            }),
            ["toolId"] = tool,
        }, cancellationToken: token);

    private static async Task<WorkbenchAddress> FocusAsync(McpClient client, WorkbenchAddress before, string tool, CancellationToken token)
    {
        var after = Context(await CallFocusAsync(client, before, tool, token).ConfigureAwait(false));
        CheckFocus(before, after, tool);
        return after;
    }

    private static void CheckFocus(WorkbenchAddress before, WorkbenchAddress after, string tool) =>
        Require(after == before with { ActiveToolId = tool, WorkbenchRevision = before.WorkbenchRevision + 1 },
            $"Expected one focus change to {tool} from {before}, got {after}.");

    private static WorkbenchAddress Context(CallToolResult result)
    {
        Require(Code(result) == "ok", $"Expected a live context, received {result.StructuredContent}.");
        return result.StructuredContent!.Value.GetProperty("context").Deserialize<WorkbenchAddress>(WireJson.Options)!;
    }

    private static string? Code(CallToolResult result) =>
        result.StructuredContent?.GetProperty("code").GetString();

    private static async Task RequireUnavailableAsync(McpClient client, WorkbenchAddress context, string transport, CancellationToken token)
    {
        try
        {
            var result = await CallFocusAsync(client, context, "output", token).ConfigureAwait(false);
            Require(result.IsError == true && result.StructuredContent is null,
                $"The open {transport} client returned a workbench result after shutdown.");
            Console.WriteLine($"{transport} after shutdown: bridge reported attachment unavailable.");
        }
        catch (Exception ex) when (ex is McpException or HttpRequestException)
        {
            Console.WriteLine($"{transport} after shutdown: transport unavailable ({ex.GetType().Name}).");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Desktop : IAsyncDisposable
    {
        private readonly Task<string> _stderr;

        public Desktop(string folder, bool remoteControl)
        {
            Folder = Directory.CreateDirectory(folder).FullName;
            var start = new ProcessStartInfo(Dotnet)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = remoteControl,
            };
            start.ArgumentList.Add(remoteControl ? typeof(RemoteControlDesktop).Assembly.Location : typeof(WorkbenchApp).Assembly.Location);
            start.ArgumentList.Add(remoteControl ? "--remote-control-desktop" : "--mcp-mutate");
            start.Environment["RETROIDE_APP_DATA_DIR"] = Folder;
            Process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the production workbench.");
            _stderr = Process.StandardError.ReadToEndAsync();
        }

        public string Folder { get; }
        public Process Process { get; }

        public async Task<string> WaitForAttachmentAsync(CancellationToken token)
        {
            var directory = Path.Combine(Folder, "attachments");
            while (!Process.HasExited)
            {
                if (Directory.Exists(directory) && Directory.GetFiles(directory, "*.json").SingleOrDefault() is { } path)
                    return path;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new InvalidOperationException($"Workbench exited during startup: {await _stderr.ConfigureAwait(false)}");
        }

        public async Task FocusThroughUiAsync(NativeInput? input, CancellationToken token)
        {
            if (input is null)
            {
                await Process.StandardInput.WriteLineAsync("focus".AsMemory(), token).ConfigureAwait(false);
                return;
            }

            input.FocusWindow(await WaitForWindowAsync(input, token).ConfigureAwait(false));
            await input.ShortcutAsync().ConfigureAwait(false);
        }

        public async Task CloseAsync(NativeInput? input, CancellationToken token)
        {
            if (input is null)
                await Process.StandardInput.WriteLineAsync("close".AsMemory(), token).ConfigureAwait(false);
            else
                input.CloseWindow(await WaitForWindowAsync(input, token).ConfigureAwait(false));
        }

        private async Task<nint> WaitForWindowAsync(NativeInput input, CancellationToken token)
        {
            while (!Process.HasExited)
            {
                var handle = input.FindWindow(Process);
                if (handle != 0)
                    return handle;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new InvalidOperationException($"Workbench exited before finding its native window: {await _stderr.ConfigureAwait(false)}");
        }

        public async Task WaitForExitAsync(CancellationToken token)
        {
            await Process.WaitForExitAsync(token).ConfigureAwait(false);
            var stderr = await _stderr.ConfigureAwait(false);
            Require(Process.ExitCode == 0, $"Workbench exited with code {Process.ExitCode}: {stderr}");
        }

        public async ValueTask DisposeAsync()
        {
            using (Process)
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
