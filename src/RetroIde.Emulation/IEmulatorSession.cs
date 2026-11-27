using System.Diagnostics.CodeAnalysis;

namespace RetroIde.Emulation;

/// <summary>Represents the connection to one emulator instance, independent of its transport.</summary>
public interface IEmulatorSession : IAsyncDisposable
{
    /// <summary>Gets the stable identity for the lifetime of this emulator instance.</summary>
    SessionIdentity Identity { get; }

    /// <summary>Reads authoritative state with a cursor from which later events can be consumed.</summary>
    ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Reads ordered control events, or reports a gap when the requested cursor cannot be resumed.</summary>
    IAsyncEnumerable<SessionEvent> ReadEventsAsync(EventCursor? after, CancellationToken cancellationToken);

    /// <summary>Resolves a port at the requested capability version when currently supported.</summary>
    bool TryGetPort<TPort>(CapabilityId capability, [NotNullWhen(true)] out TPort? port) where TPort : class, IEmulatorPort;

    /// <summary>Releases subscriptions and applies the close policy established during open.</summary>
    ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(SessionCloseRequest request, CancellationToken cancellationToken);
}
