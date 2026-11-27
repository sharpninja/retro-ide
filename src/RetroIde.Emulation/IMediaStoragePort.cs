using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Manages backend media slots and their separately transferred artifacts.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IMediaStoragePort : IEmulatorPort
{
    /// <summary>Gets the versioned media slot capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.media-storage", new Version(1, 0));
    /// <summary>Identifies media slot enumeration.</summary>
    public const string ListSlots = "list-slots";
    /// <summary>Identifies mounting an artifact in a slot.</summary>
    public const string Mount = "mount";
    /// <summary>Identifies ejecting the media in a slot.</summary>
    public const string Eject = "eject";

    /// <summary>Lists media slots and compatible formats, independently of access to host paths.</summary>
    ValueTask<AdapterResult<Slots>> ListAsync(SessionOperation scope, CancellationToken cancellationToken);
    /// <summary>Mounts a previously registered artifact or ejects the selected slot.</summary>
    ValueTask<AdapterResult<MutationReceipt>> ChangeAsync(MutationRequest<Change> request, CancellationToken cancellationToken);

    /// <summary>Describes one slot and its current media.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Slot(string Id, string Kind, ImmutableArray<string> Formats, ArtifactReference? Mounted, bool ReadOnly);
    /// <summary>Returns the current backend-advertised slots.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Slots(ImmutableArray<Slot> Items);
    /// <summary>Requests a change to a stable slot identifier.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public abstract record Change(string SlotId)
    {
        /// <summary>Mounts an artifact, using read-only mode when requested.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record Insert(string Slot, ArtifactReference Artifact, bool ReadOnly) : Change(Slot);
        /// <summary>Ejects the current media.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record Remove(string Slot) : Change(Slot);
    }
}
