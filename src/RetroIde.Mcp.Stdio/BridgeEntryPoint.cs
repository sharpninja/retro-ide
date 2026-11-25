using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using RetroIde.Mcp;

namespace RetroIde.Mcp.Stdio;

/// <summary>Runs a protocol-only stdio attachment to the selected existing IDE.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-same-instance</remarks>
public static class BridgeEntryPoint
{
    /// <summary>Accepts the path of a private per-launch descriptor.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: RetroIde.Mcp.Stdio <attachment-file>").ConfigureAwait(false);
            return 2;
        }

        try
        {
            var descriptor = AttachmentDescriptor.Read(args[0]);
            using var http = descriptor.CreateHttpClient();
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = descriptor.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
            }, http), cancellationToken: connectTimeout.Token).ConfigureAwait(false);

            await using var attachment = client.ConfigureAwait(false);

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "retro-ide", Version = "0.1" })
                .WithStdioServerTransport()
                .WithListToolsHandler((request, token) => client.ListToolsAsync(request.Params ?? new ListToolsRequestParams(), token))
                .WithCallToolHandler(async (request, token) =>
                {
                    try
                    {
                        return await client.CallToolAsync(request.Params!, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is McpException or HttpRequestException)
                    {
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = "The selected Retro IDE attachment is unavailable." }],
                        };
                    }
                });
            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or McpException or HttpRequestException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Unable to attach to the selected Retro IDE.").ConfigureAwait(false);
            return 1;
        }
    }
}
