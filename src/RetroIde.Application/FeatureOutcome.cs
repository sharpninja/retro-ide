using System.Text.Json;

namespace RetroIde.Application;

internal sealed record FeatureOutcome(string Code, string Summary, JsonElement? Data = null)
{
    internal Func<IWorkbenchRegistration, FeatureOutcome>? Commit { get; init; }
    internal IDisposable? PreparedResource { get; init; }
    internal bool KeepCapturedTarget { get; init; }

    public static FeatureOutcome Success<T>(string summary, T data) =>
        new("ok", summary, JsonSerializer.SerializeToElement(data, WireJson.Options));
}
