using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Delivers ordered, per-device guest input and releases held controls.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IInputPort : IEmulatorPort
{
    /// <summary>Gets the versioned guest input capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.input", new Version(1, 0));
    /// <summary>Identifies the delivery operation.</summary>
    public const string Send = "send";
    /// <summary>Identifies the operation ending a capture and releasing its held controls.</summary>
    public const string Release = "release";

    /// <summary>Delivers events beginning at the first sequence in the selected device and capture.</summary>
    ValueTask<AdapterResult<MutationReceipt>> SendAsync(MutationRequest<Batch> request, CancellationToken cancellationToken);
    /// <summary>Releases controls held by a capture and ends its sequence.</summary>
    ValueTask<AdapterResult<MutationReceipt>> ReleaseAsync(MutationRequest<Capture> request, CancellationToken cancellationToken);

    /// <summary>Identifies the selected input device and its capture lifetime.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Capture(string DeviceId, Guid CaptureId);
    /// <summary>Delivers a contiguous batch of input transitions or samples.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Batch(Capture Target, ulong FirstSequence, ImmutableArray<InputEvent> Events);
    /// <summary>Represents host-independent guest input data. Code names use the negotiated device convention.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public abstract record InputEvent
    {
        private InputEvent() { }
        /// <summary>Changes the state of one keyboard key.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record Key(string Code, bool Pressed) : InputEvent;
        /// <summary>Reports a pointer sample in the negotiated coordinate mode.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record Pointer(int X, int Y, int Wheel, bool Absolute, uint Buttons) : InputEvent;
        /// <summary>Changes a controller's button set and named axis values.</summary>
        /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
        public sealed record Controller(uint Buttons, ImmutableDictionary<string, short> Axes) : InputEvent;
    }
}
