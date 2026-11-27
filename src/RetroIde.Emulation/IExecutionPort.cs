namespace RetroIde.Emulation;

/// <summary>Controls guest execution through the host's negotiated operations and permissions.</summary>
public interface IExecutionPort : IEmulatorPort
{
    /// <summary>Gets the capability ID for version 1.0 of the execution port.</summary>
    public static CapabilityId Capability { get; } = new("retroide.execution", AdapterContract.CurrentVersion);

    /// <summary>Requests an execution change. The backend checks actor, revision and operation identity.</summary>
    /// <remarks>The backend must deduplicate previously applied operation IDs within the same session, including after reconnect.</remarks>
    ValueTask<AdapterResult<MutationReceipt>> ExecuteAsync(MutationRequest<ExecutionCommand> request, CancellationToken cancellationToken);
}
