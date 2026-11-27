using Microsoft.Extensions.Logging;
using RetroIde.Emulation;
using InstalledBundle = RetroIde.Application.Emulators.EmulatorBundleStore.InstalledBundle;
using OpenResult = RetroIde.Emulation.AdapterResult<RetroIde.Application.Emulators.PinnedEmulatorSession>;

namespace RetroIde.Application.Emulators;

internal sealed class EmulatorSessionCoordinator(Func<string, IEmulatorAdapterFactory> activateBinding, ILogger logger)
{
    internal async Task<OpenResult> OpenAsync(EmulatorBundleStore.BundleLease binding, EmulatorSessionPlan plan, CancellationToken token)
    {
        EmulatorBundleStore.BundleLease? lease = binding;
        IEmulatorSession? pending = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var installed = lease.Installation;
            var pin = installed.Receipt.Pin;
            var target = ResolveTarget(installed, plan.Connection);
            if (target is null)
                return Failure(AdapterErrorCodes.InvalidArgument, "The project target does not match the installed emulator bundle.");

            var factory = activateBinding(installed.EntryPoint("binding"));
            var described = await factory.DescribeAsync(token).ConfigureAwait(false);
            if (!described.Succeeded)
                return OpenResult.Failure(described.Error);

            var negotiated = AdapterContract.Negotiate(installed.Manifest.AdapterId, installed.Manifest.AdapterApi,
                [AdapterContract.CurrentVersion], described.Value, plan.Connection.Mode);
            if (!negotiated.Succeeded)
                return OpenResult.Failure(negotiated.Error);

            var request = new SessionRequest(negotiated.Value, plan.ProfileId, pin.Bundle, pin.Version, target)
            {
                ProviderSettings = plan.ProviderSettings,
            };
            var opened = await factory.OpenAsync(request, token).ConfigureAwait(false);
            if (!opened.Succeeded)
                return OpenResult.Failure(opened.Error);

            pending = opened.Value;
            var current = await ReadInitialStateAsync(pending, installed, target, plan.Connection.ClosePolicy, token).ConfigureAwait(false);
            if (!current.Succeeded)
                return OpenResult.Failure(current.Error);

            token.ThrowIfCancellationRequested();
            var session = new PinnedEmulatorSession(pending, current.Value, lease, logger);
            pending = null;
            lease = null;
            return OpenResult.Success(session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Failure(AdapterErrorCodes.Cancelled, "Opening the emulator session was cancelled.");
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "resolve and open the emulator binding");
            return Failure(AdapterErrorCodes.Failed, "The emulator binding could not be opened. Check the application log for details.");
        }
        finally
        {
            using (lease)
                if (pending is not null)
                    await PinnedEmulatorSession.ReleaseConnectionAsync(pending, logger).ConfigureAwait(false);
        }
    }

    private static SessionTarget? ResolveTarget(InstalledBundle installed, EmulatorConnection connection) => connection switch
    {
        EmulatorConnection.Launch launch when Enum.IsDefined(launch.Policy) &&
            !string.Equals(launch.EntryPoint, "binding", StringComparison.Ordinal) &&
            installed.Manifest.Targets[installed.Receipt.RuntimeIdentifier].EntryPoints.ContainsKey(launch.EntryPoint) =>
            new SessionTarget.Launch(installed.EntryPoint(launch.EntryPoint), launch.Policy) { Arguments = launch.Arguments },
        EmulatorConnection.Attach attach when !string.IsNullOrWhiteSpace(attach.EndpointReference) =>
            new SessionTarget.Attach(attach.EndpointReference, attach.ExpectedInstance),
        _ => null,
    };

    private static bool MatchesIdentity(SessionIdentity identity, InstalledBundle installed, SessionTarget target) =>
        identity.InstanceId != Guid.Empty &&
        string.Equals(identity.AdapterId, installed.Manifest.AdapterId, StringComparison.Ordinal) &&
        string.Equals(identity.BundleId, installed.Receipt.Pin.Bundle, StringComparison.Ordinal) &&
        string.Equals(identity.BundleVersion, installed.Receipt.Pin.Version, StringComparison.Ordinal) &&
        (target is not SessionTarget.Attach { ExpectedInstance: { } expected } || identity == expected);

    private static async Task<AdapterResult<SessionState>> ReadInitialStateAsync(IEmulatorSession session,
        InstalledBundle installed, SessionTarget target, SessionClosePolicy closePolicy, CancellationToken token)
    {
        var identity = session.Identity;
        if (!MatchesIdentity(identity, installed, target))
            return AdapterResult<SessionState>.Failure(new(AdapterErrorCodes.Incompatible,
                "The binding returned a different emulator instance or package identity."));

        var current = await session.GetStateAsync(token).ConfigureAwait(false);
        if (!current.Succeeded)
            return current;

        var state = current.Value;
        return state.Identity == identity && state.Cursor.Session == identity && state.Lifecycle == SessionLifecycle.Connected &&
            state.ClosePolicy == closePolicy && !state.Capabilities.Items.IsDefault
            ? current : AdapterResult<SessionState>.Failure(new(AdapterErrorCodes.Incompatible,
                "The emulator session returned inconsistent state or close policy."));
    }

    private static OpenResult Failure(string code, string message) => OpenResult.Failure(new(code, message));
}
