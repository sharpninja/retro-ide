using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Provides optional live timing observations and independently authorized throttle control.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface ITimingPort : IEmulatorPort
{
    /// <summary>Gets the versioned timing capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.timing", new Version(1, 0));
    /// <summary>Identifies the query for a clock observation.</summary>
    public const string Observe = "observe";
    /// <summary>Identifies the operation to change a speed mode.</summary>
    public const string SetThrottle = "set-throttle";
    /// <summary>Identifies bounded live bus observations, independent of timing and stepping.</summary>
    public const string ReadBus = "read-bus";

    /// <summary>Returns timing for the requested advertised clock domain.</summary>
    ValueTask<AdapterResult<Observation>> ObserveAsync(ScopedQuery<Clock> request, CancellationToken cancellationToken);

    /// <summary>Sets an advertised clock throttle mode and optional speed.</summary>
    ValueTask<AdapterResult<MutationReceipt>> SetThrottleAsync(MutationRequest<Throttle> request, CancellationToken cancellationToken);

    /// <summary>Reads recent bus observations from an independently negotiated clock or bus.</summary>
    ValueTask<AdapterResult<BusTrace>> ReadBusAsync(ScopedQuery<BusRange> request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AdapterResult<BusTrace>.Failure(new(AdapterErrorCodes.Unsupported,
            "This emulator does not advertise bus observations.", request.Scope.OperationId)));

    /// <summary>Identifies the relevant clock domain.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Clock(string Id);

    /// <summary>Reports live observed ticks, optional cycles and phase, and optional current speed.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Observation(Clock Clock, MediaTimestamp SampledAt, ulong? Cycles, string? Phase, decimal? SpeedRatio);

    /// <summary>Requests a named throttle mode for a clock and an optional multiplier.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Throttle(Clock Clock, string ModeId, decimal? SpeedRatio);
    /// <summary>Bounds a live bus query and supplies an optional sequence cursor.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record BusRange(Clock Clock, ulong? After, int Limit);
    /// <summary>Distinguishes commonly observed bus cycles.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public enum BusCycle
    {
        /// <summary>No memory transfer was observed.</summary>
        Idle,
        /// <summary>A guest read was observed.</summary>
        Read,
        /// <summary>A guest write was observed.</summary>
        Write,
    }
    /// <summary>Reports an observed bus cycle with optional physical addressing, phase and data.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record BusSample(ulong Sequence, MediaTimestamp Time, BusCycle Cycle, MemoryAddress? Address,
        string? Phase, ImmutableArray<byte> Data);
    /// <summary>Returns bounded live observations and indicates whether a cursor expired.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record BusTrace(Clock Clock, bool Gap, ImmutableArray<BusSample> Items);
}
