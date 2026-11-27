using Microsoft.Extensions.Logging;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed class PinnedEmulatorSession(IEmulatorSession session, SessionState initialState,
    EmulatorBundleStore.BundleLease bundle, ILogger logger) : IAsyncDisposable
{
    private EmulatorBundleStore.BundleLease? _bundle = bundle;

    internal IEmulatorSession Connection { get; } = session;
    internal SessionState InitialState { get; } = initialState;

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
