using System.Collections.Immutable;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal abstract record EmulatorConnection(SessionMode Mode, SessionClosePolicy ClosePolicy)
{
    internal sealed record Launch(string EntryPoint, ImmutableArray<string> Arguments, SessionClosePolicy Policy)
        : EmulatorConnection(SessionMode.Launch, Policy);

    internal sealed record Attach(string EndpointReference, SessionIdentity? ExpectedInstance = null)
        : EmulatorConnection(SessionMode.Attach, SessionClosePolicy.Detach);
}
