using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Provides scoped snapshot and replay workflows, with separate live rewind support.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface ISnapshotReplayPort : IEmulatorPort
{
    /// <summary>Gets the versioned snapshot and replay capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.snapshot-replay", new Version(1, 0));
    /// <summary>Identifies enumeration of snapshots and recordings.</summary>
    public const string List = "list";
    /// <summary>Identifies creating a snapshot.</summary>
    public const string Save = "save";
    /// <summary>Identifies restoring a snapshot.</summary>
    public const string Restore = "restore";
    /// <summary>Identifies starting or stopping a recording.</summary>
    public const string Record = "record";
    /// <summary>Identifies opening a recording.</summary>
    public const string Open = "open";
    /// <summary>Identifies a seek within the active recording.</summary>
    public const string Seek = "seek";
    /// <summary>Identifies a distinct live rewind capability.</summary>
    public const string Rewind = "rewind";

    /// <summary>Returns available snapshots and the active replay, if any.</summary>
    ValueTask<AdapterResult<Catalog>> ReadAsync(SessionOperation scope, CancellationToken cancellationToken);
    /// <summary>Performs a separately advertised snapshot or replay command.</summary>
    ValueTask<AdapterResult<MutationReceipt>> ChangeAsync(MutationRequest<Change> request, CancellationToken cancellationToken);

    /// <summary>Describes compatibility and creation time for a saved session.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Snapshot(string Id, string Label, string CompatibilityId, DateTimeOffset CreatedAt, ArtifactReference Content);
    /// <summary>Describes a recording's range, time base and independent seekability.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Replay(string Id, MediaTimestamp Start, ulong EndTick, ulong Position, bool Seekable);
    /// <summary>Returns snapshots and the optional active recording.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Catalog(ImmutableArray<Snapshot> Snapshots, Replay? ActiveReplay);
    /// <summary>Identifies a typed snapshot or replay mutation.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public abstract record Change
    {
        private Change() { }
        /// <summary>Captures a snapshot with the requested label.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record SaveSnapshot(string Label) : Change;
        /// <summary>Restores the specified compatible snapshot.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record RestoreSnapshot(string Id) : Change;
        /// <summary>Starts or stops recording.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record SetRecording(bool Enabled) : Change;
        /// <summary>Opens a registered recording.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record OpenRecording(ArtifactReference Artifact) : Change;
        /// <summary>Seeks within a seekable recording's clock and tick range.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record SeekRecording(string Id, MediaTimestamp Position) : Change;
        /// <summary>Requests a rewind of live guest execution in the selected clock domain.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record RewindLive(MediaTimestamp Position) : Change;
    }
}
