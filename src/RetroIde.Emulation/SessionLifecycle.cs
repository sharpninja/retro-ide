namespace RetroIde.Emulation;

/// <summary>Identifies the connection lifecycle reported by the adapter.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public enum SessionLifecycle
{
    /// <summary>The backend is connected and the session can accept requests.</summary>
    Connected,

    /// <summary>The backend connection was lost and may be recoverable.</summary>
    Disconnected,

    /// <summary>This IDE connection has been released.</summary>
    Closed,
}
