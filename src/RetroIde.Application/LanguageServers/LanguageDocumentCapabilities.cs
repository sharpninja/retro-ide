namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageDocumentCapabilities(Guid? SessionId, string[] Methods)
{
    internal static LanguageDocumentCapabilities Unavailable { get; } = new(null, []);

    internal bool Supports(string method) => Methods.Contains(method, StringComparer.Ordinal);
}
