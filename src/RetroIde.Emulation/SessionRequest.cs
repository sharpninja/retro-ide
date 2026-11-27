using System.Text.Json;

namespace RetroIde.Emulation;

/// <summary>Contains host-validated launch or attach data for a negotiated contract.</summary>
/// <param name="ContractVersion">Exact shared adapter API version selected for this request.</param>
/// <param name="TargetProfileId">Identifier of the captured project target profile.</param>
/// <param name="BundleId">Verified package identifier to report in the session identity.</param>
/// <param name="BundleVersion">Pinned release version to report in the session identity.</param>
/// <param name="Target">Mode-specific settings whose references the host has validated.</param>
public sealed record SessionRequest(Version ContractVersion, string TargetProfileId, string BundleId, string BundleVersion, SessionTarget Target)
{
    /// <summary>Gets optional provider-specific settings from the captured, validated profile.</summary>
    public JsonElement? ProviderSettings { get; init; }
}
