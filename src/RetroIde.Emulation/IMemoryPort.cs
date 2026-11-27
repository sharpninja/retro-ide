namespace RetroIde.Emulation;

/// <summary>Exposes separately advertised mapped and legacy raw side-effect-free inspection.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IMemoryPort : IEmulatorPort
{
    /// <summary>Gets the capability ID for the version 1.0 memory port.</summary>
    public static CapabilityId Capability { get; } = new("retroide.memory", new Version(1, 0));

    /// <summary>Identifies inspection against an exact current map revision and CPU.</summary>
    public const string InspectMapped = "inspect-mapped";

    /// <summary>Identifies bounded raw inspection with no detailed description required.</summary>
    public const string InspectRaw = "inspect-raw";

    /// <summary>Identifies a debugger write to a mapped region.</summary>
    public const string WriteMapped = "write-mapped";

    /// <summary>Reads memory without device side effects and verifies any supplied state and map preconditions at the host.</summary>
    ValueTask<AdapterResult<MemoryBlock>> ReadAsync(MemoryReadRequest request, CancellationToken cancellationToken);

    /// <summary>Writes to an authorized region of the current mapped address space when advertised.</summary>
    ValueTask<AdapterResult<MutationReceipt>> WriteAsync(MutationRequest<MemoryWriteRequest> request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.Unsupported,
            "This emulator does not advertise mapped memory writes.", request.Scope.OperationId)));
}
