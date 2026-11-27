namespace RetroIde.Emulation;

/// <summary>Defines shared adapter error codes. Optional capabilities may add their own namespaced codes.</summary>
public static class AdapterErrorCodes
{
    /// <summary>The capability or negotiated version is unsupported.</summary>
    public const string Unsupported = "unsupported";
    /// <summary>The capability is known but unavailable in the current state.</summary>
    public const string Unavailable = "unavailable";
    /// <summary>The operation's arguments are invalid.</summary>
    public const string InvalidArgument = "invalid_argument";
    /// <summary>The operation cannot run in the current guest or connection state.</summary>
    public const string InvalidState = "invalid_state";
    /// <summary>The adapter identity, contract version, or instance is incompatible with the request.</summary>
    public const string Incompatible = "incompatible";
    /// <summary>The host denied the actor's request.</summary>
    public const string PermissionDenied = "permission_denied";
    /// <summary>The state revision differs from the request's precondition.</summary>
    public const string RevisionConflict = "revision_conflict";
    /// <summary>The connection is unavailable or was interrupted.</summary>
    public const string Disconnected = "disconnected";
    /// <summary>The operation exceeded its time limit.</summary>
    public const string Timeout = "timeout";
    /// <summary>The operation was cancelled before applying its effects.</summary>
    public const string Cancelled = "cancelled";
    /// <summary>The operation failed with a known outcome.</summary>
    public const string Failed = "failed";
    /// <summary>The caller must reconcile with the host before retrying the mutation.</summary>
    public const string UnknownOutcome = "unknown_outcome";
}
