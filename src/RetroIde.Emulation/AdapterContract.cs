namespace RetroIde.Emulation;

/// <summary>Agrees on an exact .NET contract version between the IDE, installed bundle and adapter.</summary>
public static class AdapterContract
{
    /// <summary>Gets the current core contract version supported by Retro IDE.</summary>
    public static Version CurrentVersion { get; } = new(1, 0);

    /// <summary>Chooses the newest exact version all participants support for the requested mode.</summary>
    public static AdapterResult<Version> Negotiate(
        string bundleAdapterId,
        IReadOnlyCollection<Version> bundleVersions,
        IReadOnlyCollection<Version> clientVersions,
        AdapterDescriptor adapter,
        SessionMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleAdapterId);
        ArgumentNullException.ThrowIfNull(bundleVersions);
        ArgumentNullException.ThrowIfNull(clientVersions);
        ArgumentNullException.ThrowIfNull(adapter);
        if (!string.Equals(adapter.AdapterId, bundleAdapterId, StringComparison.Ordinal))
            return AdapterResult<Version>.Failure(new(AdapterErrorCodes.Incompatible, "The binding adapter ID does not match the installed bundle."));

        if (adapter.Modes.IsDefaultOrEmpty || !adapter.Modes.Contains(mode))
            return AdapterResult<Version>.Failure(new(AdapterErrorCodes.Unsupported, "The binding does not support the requested session mode."));

        Version? selected = null;
        if (!adapter.ContractVersions.IsDefaultOrEmpty)
        {
            foreach (var version in adapter.ContractVersions)
            {
                if (version is { Major: > 0, Build: -1 } && (selected is null || version > selected) &&
                    bundleVersions.Contains(version) && clientVersions.Contains(version))
                    selected = version;
            }
        }

        return selected is null
            ? AdapterResult<Version>.Failure(new(AdapterErrorCodes.Incompatible, "The IDE, installed bundle and binding have no common contract version."))
            : AdapterResult<Version>.Success(selected);
    }
}
