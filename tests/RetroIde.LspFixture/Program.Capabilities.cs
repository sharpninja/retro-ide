using System.Globalization;
using System.Text.Json;

namespace RetroIde.LspFixture;

internal sealed partial class Program
{
    private bool _dynamicDocuments;
    private int _capabilityStep;

    private void ReadCapabilityFixture(JsonElement options, JsonElement parameters)
    {
        _dynamicDocuments = options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("dynamicDocuments", out var value) && value.ValueKind == JsonValueKind.True;
        if (!_dynamicDocuments)
            return;
        var advertised = parameters.GetProperty("capabilities").GetProperty("textDocument").TryGetProperty("hover", out var hover) &&
            hover.TryGetProperty("dynamicRegistration", out var enabled) && enabled.ValueKind == JsonValueKind.True;
        Trace("dynamic-hover-client-capability", advertised.ToString(CultureInfo.InvariantCulture));
    }

    private void ChangeFixtureCapabilities(JsonElement parameters)
    {
        if (!_dynamicDocuments)
            return;

        var hover = new
        {
            id = "fixture-hover",
            method = "textDocument/hover",
            registerOptions = new { documentSelector = new[] { new { language = "c", scheme = "file", pattern = "src/**/[!x]ource.{c,h}" } } },
        };
        switch (++_capabilityStep)
        {
            case 1:
                connection.Send(new { jsonrpc = "2.0", id = "fixture-capabilities-invalid", method = "client/registerCapability",
                    @params = new { registrations = new object[] { hover, new { id = "fixture-invalid", method = "textDocument/notSupported" } } } });
                break;
            case 2:
                connection.Send(new { jsonrpc = "2.0", id = "fixture-capabilities-registered", method = "client/registerCapability",
                    @params = new { registrations = new[] { hover } } });
                break;
            case 3:
                connection.Send(new { jsonrpc = "2.0", id = "fixture-capabilities-unregistered", method = "client/unregisterCapability",
                    @params = new { unregisterations = new[] { new { hover.id, hover.method } } } });
                break;
            case 4:
                var directory = Path.GetDirectoryName(new Uri(DocumentUri(parameters)).LocalPath)!;
                var baseUri = new Uri(directory).AbsoluteUri;
                var relative = new { language = "c", pattern = new { baseUri = new { uri = baseUri, name = "fixture" }, pattern = "s?urce.c" } };
                connection.Send(new { jsonrpc = "2.0", id = "fixture-capabilities-relative", method = "client/registerCapability",
                    @params = new { registrations = new[] { new { hover.id, hover.method, registerOptions = new { documentSelector = new[] { relative } } } } } });
                break;
        }
    }

    private void ObserveCapabilityResponse(JsonElement packet)
    {
        if (packet.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
            id.GetString() is "fixture-capabilities-invalid" or "fixture-capabilities-registered" or "fixture-capabilities-unregistered" or "fixture-capabilities-relative")
            Trace(id.GetString()!, packet.GetRawText());
    }
}
