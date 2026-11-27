using System.Text.Json.Serialization;

namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageServerEditReply(bool Applied,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureReason = null);
