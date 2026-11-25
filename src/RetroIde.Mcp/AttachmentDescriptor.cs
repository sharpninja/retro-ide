using System.Net;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using RetroIde.Application;

namespace RetroIde.Mcp;

/// <summary>Private per-launch descriptor used to attach to one running workbench.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-scope; TEST-RETRO-015#ac-test-mcp-same-instance</remarks>
public sealed class AttachmentDescriptor
{
    private readonly string _token;

    internal AttachmentDescriptor(Uri endpoint, string token)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !string.Equals(endpoint.Host, IPAddress.Loopback.ToString(), StringComparison.Ordinal) ||
            !string.Equals(endpoint.AbsolutePath, "/mcp", StringComparison.Ordinal) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.Port is <= 0 || token.Length != 64 || !token.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid local MCP attachment descriptor.");

        Endpoint = endpoint;
        _token = token;
    }

    /// <summary>Gets the local Streamable HTTP endpoint.</summary>
    public Uri Endpoint { get; }

    internal string Token => _token;

    /// <summary>Reads an attachment descriptor selected by the user or client configuration.</summary>
    public static AttachmentDescriptor Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 4096)
            throw new InvalidDataException("Invalid local MCP attachment descriptor.");
        var data = JsonSerializer.Deserialize<DescriptorData>(stream, WireJson.Options)
            ?? throw new InvalidDataException("Invalid local MCP attachment descriptor.");
        return new AttachmentDescriptor(data.Endpoint, data.Token);
    }

    /// <summary>Creates a new owner-only descriptor file.</summary>
    public void Write(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".attach-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = CreatePrivateFile(temporary))
                JsonSerializer.Serialize(stream, new DescriptorData(Endpoint, _token), WireJson.Options);
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Creates a client for direct MCP access, with redirects and proxies disabled.</summary>
    public HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return client;
    }

    private static FileStream CreatePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var user = WindowsIdentity.GetCurrent();
            var owner = user.User ?? throw new InvalidOperationException("A Windows user is required for MCP attachment.");
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        }

        return new FileStream(path, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    private sealed record DescriptorData(Uri Endpoint, string Token);
}
