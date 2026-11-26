namespace RetroIde.Testing;

internal sealed record AdapterFixtureSession(Guid Id, string Status, int Revision);

internal sealed record AdapterFixtureSnapshot(
    Guid WorkspaceId,
    int ProcessId,
    AdapterFixtureSession Session,
    string[] Effects,
    string[] LoadedAssemblies);
