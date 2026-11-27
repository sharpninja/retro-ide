namespace RetroIde.Emulation;

/// <summary>Exposes separately advertised mapped and legacy raw side-effect-free inspection.</summary>
public interface IMemoryPort : IEmulatorPort
{
    /// <summary>Gets the capability ID for the version 1.0 memory port.</summary>
    public static CapabilityId Capability { get; } = new("retroide.memory", new Version(1, 0));

    /// <summary>Identifies inspection against an exact current map revision and CPU.</summary>
    public const string InspectMapped = "inspect-mapped";

    /// <summary>Identifies bounded raw inspection with no detailed description required.</summary>
    public const string InspectRaw = "inspect-raw";

    /// <summary>Reads memory without device side effects and verifies any supplied state and map preconditions at the host.</summary>
    ValueTask<AdapterResult<MemoryBlock>> ReadAsync(MemoryReadRequest request, CancellationToken cancellationToken);
}
