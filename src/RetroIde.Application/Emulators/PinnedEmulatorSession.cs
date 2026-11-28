using Microsoft.Extensions.Logging;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed class PinnedEmulatorSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger _logger;
    private EmulatorBundleStore.BundleLease? _bundle;

    internal PinnedEmulatorSession(IEmulatorSession session, SessionState initialState,
        EmulatorBundleStore.BundleLease bundle, ILogger logger)
    {
        _bundle = bundle;
        _logger = logger;
        Connection = session;
        InitialState = initialState;
        Control = new(session, initialState, logger, _lifetime.Token);
    }

    internal IEmulatorSession Connection { get; }
    internal SessionState InitialState { get; }
    internal EmulatorSessionControl Control { get; }

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
            RuntimeFailures.Report(_logger, error, "read pinned debugger profile");
            return AdapterResult<DebuggerPresentationProfile>.Failure(new(AdapterErrorCodes.Incompatible,
                "The pinned bundle's debugger profile could not be verified."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _bundle, null) is not { } owned)
            return;

        using (owned)
        using (_lifetime)
        {
            try { await _lifetime.CancelAsync().ConfigureAwait(false); }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(_logger, error, "cancel emulator session subscriptions");
            }
            finally { await ReleaseConnectionAsync(Connection, _logger).ConfigureAwait(false); }
        }
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
