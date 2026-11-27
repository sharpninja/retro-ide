using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Provides separately scoped guest console, backend diagnostics and artifacts.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IConsolePort : IEmulatorPort
{
    /// <summary>Gets the versioned console capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.console", new Version(1, 0));
    /// <summary>Identifies enumeration of output and input channels.</summary>
    public const string List = "list";
    /// <summary>Identifies paged reads of a channel.</summary>
    public const string Read = "read";
    /// <summary>Identifies a live subscription to a channel.</summary>
    public const string Subscribe = "subscribe";
    /// <summary>Identifies delivery of bytes to a guest input channel.</summary>
    public const string Send = "send";
    /// <summary>Identifies enumeration of diagnostic artifacts.</summary>
    public const string ListArtifacts = "list-artifacts";

    /// <summary>Lists guest and backend channels, whose origins remain distinct.</summary>
    ValueTask<AdapterResult<Channels>> ListAsync(SessionOperation scope, CancellationToken cancellationToken);
    /// <summary>Reads bounded records from a channel's independent cursor.</summary>
    ValueTask<AdapterResult<Records>> ReadAsync(ScopedQuery<ChannelRead> request, CancellationToken cancellationToken);
    /// <summary>Streams ordered channel records, reporting a gap when records expired.</summary>
    IAsyncEnumerable<Records> ObserveAsync(ScopedQuery<ChannelRead> request, CancellationToken cancellationToken);
    /// <summary>Delivers data to a channel that has separately advertised input support.</summary>
    ValueTask<AdapterResult<MutationReceipt>> SendAsync(MutationRequest<ChannelInput> request, CancellationToken cancellationToken);
    /// <summary>Lists transferable diagnostics references and their metadata.</summary>
    ValueTask<AdapterResult<Artifacts>> ListArtifactsAsync(SessionOperation scope, CancellationToken cancellationToken);

    /// <summary>Distinguishes guest output from host diagnostics.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public enum Origin
    {
        /// <summary>Guest serial or console output.</summary>
        Guest,
        /// <summary>Emulator or worker diagnostics.</summary>
        Backend,
    }
    /// <summary>Describes one advertised channel.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Channel(string Id, Origin Source, string Encoding, bool AcceptsInput);
    /// <summary>Lists available channels.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Channels(ImmutableArray<Channel> Items);
    /// <summary>Requests a bounded range or subscription resume point.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record ChannelRead(string ChannelId, ulong? After, int Limit);
    /// <summary>Carries an ordered channel entry with its producer's clock and optional log level.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Entry(ulong Sequence, MediaTimestamp Time, string? Level, ImmutableArray<byte> Data);
    /// <summary>Returns a page of records and flags an expired cursor.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Records(string ChannelId, bool Gap, ImmutableArray<Entry> Items);
    /// <summary>Specifies guest input data and the channel encoding it follows.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record ChannelInput(string ChannelId, ImmutableArray<byte> Data);
    /// <summary>Describes one registered diagnostic artifact.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Artifact(ArtifactReference Reference, string Name, string MediaType, long? ByteCount);
    /// <summary>Returns all advertised diagnostic artifacts.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Artifacts(ImmutableArray<Artifact> Items);
}
