namespace RetroIde.Application.Emulators;

internal interface IEmulatorRegistration
{
    EmulatorWorkspace? Emulators { get; }
    void EmulatorPackagesChanged();
}
