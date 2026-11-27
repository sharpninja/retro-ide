using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace RetroIde.IntegrationTests;

// A private fixture front end for Git's real smart HTTP service.
// https://git-scm.com/docs/git-http-backend#_environment
internal sealed class AuthenticatedGitRemote(string repository, string password)
{
    private const string Prefix = "/fixture.git";
    private readonly string _authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture:" + password));
    private int _denials;
    internal int Denials => Volatile.Read(ref _denials);

    internal async Task<WebApplication> StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet(Prefix + "/info/refs", context => HandleAsync(context, "/info/refs"));
        app.MapPost(Prefix + "/git-upload-pack", context => HandleAsync(context, "/git-upload-pack"));
        app.MapPost(Prefix + "/git-receive-pack", context => HandleAsync(context, "/git-receive-pack"));
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            return app;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static string RemoteUrl(WebApplication app) => app.Urls.Single() + Prefix;

    private async Task HandleAsync(HttpContext context, string suffix)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!string.Equals(authorization, _authorization, StringComparison.Ordinal))
        {
            if (authorization.Length > 0)
                Interlocked.Increment(ref _denials);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"retro-ide-git-fixture\"";
            return;
        }

        var token = context.RequestAborted;
        using var process = Process.Start(Backend(context.Request, suffix))
            ?? throw new InvalidOperationException("Could not start the fixture Git HTTP backend.");
        using var response = new MemoryStream();
        var output = process.StandardOutput.BaseStream.CopyToAsync(response, token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await context.Request.Body.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            var diagnostic = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Git HTTP backend exited with {process.ExitCode}. {diagnostic}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        await WriteCgiResponseAsync(context.Response, response.ToArray(), token).ConfigureAwait(false);
    }

    private ProcessStartInfo Backend(HttpRequest request, string suffix)
    {
        var start = new ProcessStartInfo("git", "http-backend")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(repository, "unused-fixture-config");
        start.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        start.Environment["PATH_TRANSLATED"] = repository.Replace('\\', '/') + suffix;
        start.Environment["REMOTE_USER"] = "fixture";
        start.Environment["REMOTE_ADDR"] = "127.0.0.1";
        start.Environment["REQUEST_METHOD"] = request.Method;
        start.Environment["QUERY_STRING"] = (request.QueryString.Value ?? "").TrimStart('?');
        start.Environment["CONTENT_TYPE"] = request.ContentType ?? "";
        start.Environment["CONTENT_LENGTH"] = request.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "";
        start.Environment["HTTP_CONTENT_ENCODING"] = request.Headers.ContentEncoding.ToString();
        start.Environment["HTTP_GIT_PROTOCOL"] = request.Headers["Git-Protocol"].ToString();
        return start;
    }

    private static Task WriteCgiResponseAsync(HttpResponse response, byte[] bytes, CancellationToken token)
    {
        var headerLength = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
        if (headerLength < 0)
            throw new InvalidDataException("The fixture backend did not terminate its CGI headers.");

        var headers = Encoding.ASCII.GetString(bytes, 0, headerLength);
        foreach (var line in headers.Split("\r\n", StringSplitOptions.None))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
                throw new InvalidDataException("The fixture backend returned an invalid CGI header.");
            var name = line[..separator];
            var value = line[(separator + 1)..].Trim();
            if (string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase))
                response.StatusCode = int.Parse(value.AsSpan(0, 3), CultureInfo.InvariantCulture);
            else
                response.Headers.Append(name, value);
        }

        return response.Body.WriteAsync(bytes.AsMemory(headerLength + 4), token).AsTask();
    }
}
