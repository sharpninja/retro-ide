using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Separates launch and attach settings while keeping credentials in the host's configuration.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public abstract record SessionTarget
{
    private SessionTarget() { }

    /// <summary>A resolved path from a trusted installed package and the requested lifetime policy.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
    public sealed record Launch(string ExecutablePath, SessionClosePolicy ClosePolicy) : SessionTarget
    {
        /// <summary>Gets individual validated arguments to pass directly to the process.</summary>
        public ImmutableArray<string> Arguments { get; init; } = [];
    }

    /// <summary>A configured endpoint reference and the identity expected when reconnecting.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
    public sealed record Attach(string EndpointReference, SessionIdentity? ExpectedInstance = null) : SessionTarget;
}
