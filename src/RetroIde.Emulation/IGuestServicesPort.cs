using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Reports optional guest services and container metadata with scoped backend authorization.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IGuestServicesPort : IEmulatorPort
{
    /// <summary>Gets the versioned guest services capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.guest-services", new Version(1, 0));
    /// <summary>Identifies service and container enumeration.</summary>
    public const string List = "list";

    /// <summary>Lists available guest services and the runtime context reported by the emulator.</summary>
    ValueTask<AdapterResult<Snapshot>> ReadAsync(SessionOperation scope, CancellationToken cancellationToken);

    /// <summary>Describes a service and its optional container.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Service(string Id, string Kind, string State, string? ContainerId);
    /// <summary>Describes a guest container without imposing a host runtime.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Container(string Id, string Name, string Runtime, string State);
    /// <summary>Reports the guest's currently visible services and containers.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Snapshot(ImmutableArray<Service> Services, ImmutableArray<Container> Containers);
}
