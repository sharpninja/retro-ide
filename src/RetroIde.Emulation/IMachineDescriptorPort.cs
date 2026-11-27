namespace RetroIde.Emulation;

/// <summary>Reports an authoritative, optional debugger description of the current target.</summary>
public interface IMachineDescriptorPort : IEmulatorPort
{
    /// <summary>Gets the capability ID for the version 1.0 descriptor format.</summary>
    public static CapabilityId Capability { get; } = new("retroide.machine-descriptor", new Version(1, 0));

    /// <summary>Identifies the machine-description query in the negotiated capability.</summary>
    public const string Describe = "describe";

    /// <summary>Returns the current machine and active memory map for the scoped requester.</summary>
    ValueTask<AdapterResult<MachineDescriptor>> DescribeAsync(SessionOperation scope, CancellationToken cancellationToken);
}
