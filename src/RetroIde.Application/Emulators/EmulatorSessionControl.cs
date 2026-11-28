using Microsoft.Extensions.Logging;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    private readonly SemaphoreSlim _mutations = new(1);
    private readonly IEmulatorSession _connection;
    private readonly ILogger _logger;
    private readonly SessionIdentity _identity;
    private readonly SessionClosePolicy _closePolicy;
    private readonly CancellationToken _sessionLifetime;
    // The backend owns normal operation receipts and keeps them across connections.
    // Retain the uncertain operations this connection has explicitly reconciled.
    private readonly Dictionary<Guid, (PendingMutation Request, MutationResolution Receipt)> _resolutions = [];
    private SessionState _state;
    private PendingMutation? _uncertain;

    internal EmulatorSessionControl(IEmulatorSession connection, SessionState initialState, ILogger logger,
        CancellationToken sessionLifetime = default)
    {
        _connection = connection;
        _identity = initialState.Identity;
        _closePolicy = initialState.ClosePolicy;
        _state = initialState;
        _logger = logger;
        _sessionLifetime = sessionLifetime;
    }

    internal SessionState State => Volatile.Read(ref _state);

    internal async ValueTask<AdapterResult<SessionState>> ReadAsync(CancellationToken token)
    {
        if (_sessionLifetime.IsCancellationRequested)
            return Fail<SessionState>(AdapterErrorCodes.Disconnected, "The emulator connection has closed.");

        try
        {
            var previous = State;
            var result = await _connection.GetStateAsync(token).ConfigureAwait(false);
            if (!result.Succeeded)
                return result;

            var state = result.Value;
            if (!Matches(state) || IsOlder(state, previous))
                return Fail<SessionState>(AdapterErrorCodes.Incompatible, "The state snapshot belongs to a different or incompatible session.");

            var published = Publish(state);
            return ReferenceEquals(published, state) ? result : AdapterResult<SessionState>.Success(published);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail<SessionState>(AdapterErrorCodes.Cancelled, "Reading emulator state was cancelled.");
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "read emulator state");
            return Fail<SessionState>(AdapterErrorCodes.Disconnected, "The emulator state could not be read.");
        }
    }

    private bool Matches(SessionState state) =>
        state.Identity == _identity && state.Cursor.Session == state.Identity &&
        state.ClosePolicy == _closePolicy && !state.Capabilities.Items.IsDefault;

    private SessionState Publish(SessionState incoming)
    {
        while (true)
        {
            var current = State;
            if (IsOlder(incoming, current))
                return current;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, incoming, current), current))
                return incoming;
        }
    }

    private static bool IsOlder(SessionState incoming, SessionState current) =>
        incoming.StateRevision < current.StateRevision || incoming.Cursor.Sequence < current.Cursor.Sequence ||
        incoming.Capabilities.Revision < current.Capabilities.Revision;

    private AdapterError? ValidateScope(SessionOperation scope) =>
        scope.Session != _identity
            ? new(AdapterErrorCodes.Incompatible, "The operation targets a different emulator session.", scope.OperationId)
            : scope.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(scope.ActorId)
                ? new(AdapterErrorCodes.InvalidArgument, "The operation requires an ID and actor.", scope.OperationId)
                : null;

    private static CapabilityDescriptor? Advertised(SessionState state, CapabilityId capability, string? subject, string operation) =>
        state.Capabilities.Items.FirstOrDefault(item => item.Id == capability &&
            string.Equals(item.SubjectId, subject, StringComparison.Ordinal) && item.Operations.Contains(operation, StringComparer.Ordinal));

    private static AdapterResult<T> Fail<T>(string code, string message, Guid? operation = null, ulong? revision = null) where T : class =>
        AdapterResult<T>.Failure(new(code, message, operation, revision));
}
