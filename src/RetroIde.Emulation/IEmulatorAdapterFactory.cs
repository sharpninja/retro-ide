namespace RetroIde.Emulation;

/// <summary>Publishes an adapter's compatibility and opens a negotiated emulator session.</summary>
/// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
public interface IEmulatorAdapterFactory
{
    /// <summary>Returns local compatibility information without opening the emulator.</summary>
    ValueTask<AdapterResult<AdapterDescriptor>> DescribeAsync(CancellationToken cancellationToken);

    /// <summary>Launches or attaches and returns a session only once it is ready.</summary>
    ValueTask<AdapterResult<IEmulatorSession>> OpenAsync(SessionRequest request, CancellationToken cancellationToken);
}
