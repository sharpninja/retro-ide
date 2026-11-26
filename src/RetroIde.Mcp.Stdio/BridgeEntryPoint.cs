using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using RetroIde.Application;
using RetroIde.Mcp;

namespace RetroIde.Mcp.Stdio;

/// <summary>Runs a protocol-only stdio attachment to the selected existing IDE.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-same-instance</remarks>
public static class BridgeEntryPoint
{
    /// <summary>Accepts the path of a private per-launch descriptor.</summary>
    public static async Task<int> Main(string[] args)
    {
        using var logs = LoggerFactory.Create(builder => ConfigureLogging(builder));
        var logger = logs.CreateLogger("RetroIde.Mcp.Stdio");
        using var exceptions = new ProcessExceptionHandlers(logger);
        try
        {
            if (args.Length != 1)
            {
                await Console.Error.WriteLineAsync("Usage: RetroIde.Mcp.Stdio <attachment-file>").ConfigureAwait(false);
                return 2;
            }

            var descriptor = AttachmentDescriptor.Read(args[0]);
            using var http = descriptor.CreateHttpClient();
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = descriptor.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
            }, http), loggerFactory: logs, cancellationToken: connectTimeout.Token).ConfigureAwait(false);

            await using var attachment = client.ConfigureAwait(false);

            var builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder.Logging);
            builder.Services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "retro-ide", Version = "0.1" })
                .WithStdioServerTransport()
                .WithListToolsHandler(async (request, token) =>
                {
                    try { return await client.ListToolsAsync(request.Params ?? new ListToolsRequestParams(), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        ReportRequestFailure(logger, error, "stdio tool discovery");
                        throw new McpProtocolException("The selected Retro IDE attachment is unavailable.", McpErrorCode.InternalError);
                    }
                })
                .WithCallToolHandler(async (request, token) =>
                {
                    if (request.Params is not { } args)
                        throw new McpProtocolException("Tool arguments are required.", McpErrorCode.InvalidParams);
                    try
                    {
                        return await client.CallToolAsync(args, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        ReportRequestFailure(logger, error, "stdio tool forwarding");
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
        catch (Exception error)
        {
            RuntimeFailures.Report(logger, error, "Unable to attach to the selected Retro IDE", LogLevel.Critical);
            return 1;
        }
    }

    private static void ConfigureLogging(ILoggingBuilder logging) => logging.ClearProviders()
        .SetMinimumLevel(LogLevel.Warning)
        .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    private static void ReportRequestFailure(ILogger logger, Exception error, string boundary)
    {
        var recoverable = RuntimeFailures.CanRecover(error);
        RuntimeFailures.Report(logger, error, boundary, recoverable ? LogLevel.Error : LogLevel.Critical);
        if (!recoverable)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
    }
}
