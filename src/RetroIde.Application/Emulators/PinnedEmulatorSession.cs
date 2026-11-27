using Microsoft.Extensions.Logging;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed class PinnedEmulatorSession(IEmulatorSession session, SessionState initialState,
    EmulatorBundleStore.BundleLease bundle, ILogger logger) : IAsyncDisposable
{
    private EmulatorBundleStore.BundleLease? _bundle = bundle;

    internal IEmulatorSession Connection { get; } = session;
    internal SessionState InitialState { get; } = initialState;
    internal EmulatorSessionControl Control { get; } = new(session, initialState, logger);

    internal AdapterResult<DebuggerPresentationProfile> ReadDebuggerProfile(MachineDescriptor machine)
    {
        if (machine.Session != InitialState.Identity || machine.Version != IMachineDescriptorPort.Capability.Version ||
            !Control.IsCurrentMap(machine.MapRevision))
            return AdapterResult<DebuggerPresentationProfile>.Failure(new(AdapterErrorCodes.Incompatible,
                "The debugger profile request is for a different or older emulator target."));
        if (_bundle is not { } owned || machine.Profile is not { } profile)
            return AdapterResult<DebuggerPresentationProfile>.Failure(new(AdapterErrorCodes.Unavailable,
                "This emulator target has no active debugger profile."));
        try { return owned.Installation.ReadDebuggerProfile(profile); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "read pinned debugger profile");
            return AdapterResult<DebuggerPresentationProfile>.Failure(new(AdapterErrorCodes.Incompatible,
                "The pinned bundle's debugger profile could not be verified."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _bundle, null) is not { } owned)
            return;

        using (owned)
            await ReleaseConnectionAsync(Connection, logger).ConfigureAwait(false);
    }

    internal static async ValueTask ReleaseConnectionAsync(IEmulatorSession connection, ILogger logger)
    {
        try { await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "emulator session disposal");
        }
    }
}
