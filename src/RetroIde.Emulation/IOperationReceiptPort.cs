namespace RetroIde.Emulation;

/// <summary>Allows the original actor to ask the backend what happened to a scoped mutation.</summary>
/// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
public interface IOperationReceiptPort : IEmulatorPort
{
    /// <summary>Gets the capability ID for version 1.0 of the reconciliation port.</summary>
    public static CapabilityId Capability { get; } = new("retroide.operation-receipts", new Version(1, 0));

    /// <summary>Identifies the reconciliation operation in the advertised capability.</summary>
    public const string Reconcile = "reconcile";

    /// <summary>Checks the host's recorded outcome without performing or repeating the mutation.</summary>
    ValueTask<AdapterResult<MutationResolution>> ReconcileAsync(SessionOperation operation, CancellationToken cancellationToken);
}
