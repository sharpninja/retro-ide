using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RetroIde.Application;

namespace RetroIde.Mcp;

/// <summary>Hosts authenticated, local Streamable HTTP on the selected workbench process.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-same-instance; TEST-RETRO-015#ac-test-mcp-scope</remarks>
public sealed partial class LoopbackMcpHost : IAsyncDisposable
{
    private static readonly object GrantKey = new();
    private readonly WebApplication _app;
    private readonly Func<Uri> _endpoint;
    private readonly WorkbenchAddress _scope;
    private readonly ConcurrentDictionary<string, ActorGrant> _grants = new(StringComparer.Ordinal);
    private readonly Lock _lifecycle = new();
    private bool _disposed;

    private LoopbackMcpHost(WebApplication app, WorkbenchAddress scope)
    {
        _app = app;
        _scope = scope;
        _endpoint = () => new Uri(new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), "/mcp");
    }

    /// <summary>Starts the official SDK on an OS-assigned IPv4 loopback port.</summary>
    public static async Task<LoopbackMcpHost> StartAsync(FeatureRegistry registry, IWorkbenchDispatcher dispatcher,
        WorkbenchAddress scope, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter(typeof(LoopbackMcpHost).FullName, LogLevel.Information);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Configure(new ConfigurationBuilder().Build());
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 32 * 1024;
        });
        builder.Services.AddHttpContextAccessor();
        ConfigureTools(builder.Services, registry, dispatcher);

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<LoopbackMcpHost>>();
        var host = new LoopbackMcpHost(app, scope);
        try
        {
            app.Use(host.AuthenticateAsync);
            app.MapMcp("/mcp");
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            try { await host.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(logger, error, "MCP host startup cleanup");
            }
            throw;
        }
    }

    private static void ConfigureTools(IServiceCollection services, FeatureRegistry registry, IWorkbenchDispatcher dispatcher)
    {
        var features = registry.Features.ToDictionary(feature => feature.ToolName, StringComparer.Ordinal);
        var tools = registry.Features.Select(feature =>
        {
            var schemas = registry.Describe(feature);
            return new Tool
            {
                Name = feature.ToolName,
                Description = feature.Description,
                InputSchema = schemas.InputSchema,
                OutputSchema = schemas.OutputSchema,
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = !feature.IsMutation,
                    DestructiveHint = false,
                    OpenWorldHint = false,
                },
            };
        }).ToArray();

        services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "retro-ide", Version = "0.1" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = tools.ToList() }))
            .WithCallToolHandler(async (request, token) =>
            {
                var operation = Guid.NewGuid();
                var provider = request.Server.Services!;
                var http = provider.GetRequiredService<IHttpContextAccessor>().HttpContext;
                if (http?.Items[GrantKey] is not ActorGrant grant)
                    return ToWire(OperationResult.Failure(operation, "unauthorized", "Authentication is required."));

                var logger = provider.GetRequiredService<ILogger<LoopbackMcpHost>>();
                var tool = "<unknown>";
                try
                {
                    var args = request.Params;
                    var feature = args is not null && features.TryGetValue(args.Name, out var found) ? found : null;
                    tool = feature?.ToolName ?? tool;
                    var result = feature is null
                        ? OperationResult.Failure(operation, "tool_unavailable", "The requested tool is unavailable.")
                        : await feature.InvokeAsync(dispatcher, registry.Context, args!.Arguments, grant, operation, token).ConfigureAwait(false);
                    Audit(logger, tool, grant.Actor, grant.WorkspaceId, grant.ViewKey, operation, result.Code);
                    return ToWire(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    var recoverable = RuntimeFailures.CanRecover(error);
                    RuntimeFailures.Report(logger, error, $"MCP tool {tool} ({operation})", recoverable ? LogLevel.Error : LogLevel.Critical);
                    if (!recoverable)
                        throw;
                    return ToWire(OperationResult.Failure(operation, "internal_error", "The selected operation failed."));
                }
            });
    }

    /// <summary>Issues a revocable grant for the selected view, with a distinct per-client secret.</summary>
    public AttachmentDescriptor CreateAttachment(string actor, bool canMutate)
    {
        if (actor.Length is < 1 or > 64 || !actor.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new ArgumentException("Use a stable actor ID containing letters, digits, '.', '-' or '_'.", nameof(actor));

        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var descriptor = new AttachmentDescriptor(_endpoint(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            if (!_grants.TryAdd(TokenKey(descriptor.Token), new ActorGrant(actor, _scope.WorkspaceId, _scope.ViewKey, canMutate)))
                throw new InvalidOperationException("Could not issue an MCP attachment.");
            return descriptor;
        }
    }

    /// <summary>Revokes an issued descriptor, including queued operations.</summary>
    public void Revoke(AttachmentDescriptor descriptor)
    {
        if (_grants.TryGetValue(TokenKey(descriptor.Token), out var grant))
        {
            grant.Revoke();
            _grants.TryRemove(TokenKey(descriptor.Token), out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var grant in _grants.Values)
                grant.Revoke();
            _grants.Clear();
        }

        try { await _app.StopAsync().ConfigureAwait(false); }
        finally { await _app.DisposeAsync().ConfigureAwait(false); }
    }

    private Task AuthenticateAsync(HttpContext context, RequestDelegate next)
    {
        var expectedHost = context.Connection.LocalIpAddress?.ToString() + ":" + context.Connection.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var origins = context.Request.Headers.Origin;
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
            !string.Equals(context.Request.Host.Value, expectedHost, StringComparison.Ordinal) ||
            (origins.Count != 0 && (origins.Count != 1 || !string.Equals(origins[0], "http://" + expectedHost, StringComparison.Ordinal))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        if (context.Request.Headers.Authorization.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization[0], out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is not { Length: 64 } token || !token.All(char.IsAsciiHexDigit) ||
            !_grants.TryGetValue(TokenKey(token), out var grant) || !grant.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }

        context.Items[GrantKey] = grant;
        return next(context);
    }

    private static string TokenKey(string token) => Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token)));

    private static CallToolResult ToWire(OperationResult result) => new()
    {
        IsError = !string.Equals(result.Code, "ok", StringComparison.Ordinal),
        Content = [new TextContentBlock { Text = result.Summary }],
        StructuredContent = JsonSerializer.SerializeToElement(result, WireJson.Options),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP {Tool} actor={Actor} workspace={Workspace} view={View} operation={Operation} outcome={Outcome}")]
    private static partial void Audit(ILogger logger, string tool, string actor, Guid workspace, Guid view, Guid operation, string outcome);
}
