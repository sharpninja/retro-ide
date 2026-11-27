using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Reads and writes registers of an exact CPU instance using its negotiated layout.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IRegisterPort : IEmulatorPort
{
    /// <summary>Gets the versioned register capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.registers", new Version(1, 0));
    /// <summary>Identifies the register read operation.</summary>
    public const string Read = "read";
    /// <summary>Identifies the register mutation.</summary>
    public const string Write = "write";

    /// <summary>Reads the requested registers or returns the CPU's advertised register set if the list is empty.</summary>
    ValueTask<AdapterResult<Snapshot>> ReadAsync(ScopedQuery<Selection> request, CancellationToken cancellationToken);
    /// <summary>Requests an authorized write to the selected registers.</summary>
    ValueTask<AdapterResult<MutationReceipt>> WriteAsync(MutationRequest<Update> request, CancellationToken cancellationToken);

    /// <summary>Names a CPU and an optional subset of its register identifiers.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Selection(string CpuId, ImmutableArray<string> RegisterIds);
    /// <summary>Reports register width, canonical bytes in most-significant-byte-first order and access policy.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record RegisterValue(string Id, int WidthBits, ImmutableArray<byte> Value, bool Writable);
    /// <summary>Returns the values at an observed state revision.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Snapshot(string CpuId, ulong StateRevision, ImmutableArray<RegisterValue> Values);
    /// <summary>Names a CPU and the values to write.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Update(string CpuId, ImmutableArray<RegisterValue> Values);
}
