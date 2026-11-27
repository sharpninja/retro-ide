using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal ValueTask<AdapterResult<MutationReceipt>> SendInputAsync(MutationRequest<IInputPort.Batch> request, CancellationToken token)
    {
        var batch = request.Payload;
        if (!ValidCapture(batch.Target) || batch.Events.IsDefaultOrEmpty || batch.Events.Length > 1024 ||
            batch.FirstSequence > ulong.MaxValue - (ulong)(batch.Events.Length - 1) || !batch.Events.All(ValidInput))
            return InvalidPortInput<MutationReceipt>(request.Scope);

        return MutatePortAsync<IInputPort>(new(request.Scope, IInputPort.Capability, batch.Target.DeviceId, IInputPort.Send),
            (port, cancellation) => port.SendAsync(request, cancellation),
            (capability, _) => CheckAdvertisedLimit(capability, request.Scope, "max-events", (ulong)batch.Events.Length, "input batch"),
            token, requireRevision: false);
    }

    internal ValueTask<AdapterResult<MutationReceipt>> ReleaseInputAsync(MutationRequest<IInputPort.Capture> request, CancellationToken token) =>
        ValidCapture(request.Payload)
            ? MutatePortAsync<IInputPort>(new(request.Scope, IInputPort.Capability, request.Payload.DeviceId, IInputPort.Release),
                (port, cancellation) => port.ReleaseAsync(request, cancellation), null, token, requireRevision: false)
            : InvalidPortInput<MutationReceipt>(request.Scope);

    internal ValueTask<AdapterResult<IMediaStoragePort.Slots>> ListMediaSlotsAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<IMediaStoragePort, IMediaStoragePort.Slots>(scope, IMediaStoragePort.Capability, null, IMediaStoragePort.ListSlots,
            (port, _) => port.ListAsync(scope, token), token);

    internal ValueTask<AdapterResult<MutationReceipt>> ChangeMediaAsync(MutationRequest<IMediaStoragePort.Change> request, CancellationToken token)
    {
        var operation = request.Payload switch
        {
            IMediaStoragePort.Change.Insert insert when ValidArtifact(insert.Artifact) => IMediaStoragePort.Mount,
            IMediaStoragePort.Change.Remove => IMediaStoragePort.Eject,
            _ => null,
        };
        return operation is not null && MachineMap.Identifier(request.Payload.SlotId)
            ? MutatePortAsync<IMediaStoragePort>(new(request.Scope, IMediaStoragePort.Capability, request.Payload.SlotId, operation),
                (port, cancellation) => port.ChangeAsync(request, cancellation), null, token)
            : InvalidPortInput<MutationReceipt>(request.Scope);
    }

    internal ValueTask<AdapterResult<ISnapshotReplayPort.Catalog>> ReadSnapshotsAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<ISnapshotReplayPort, ISnapshotReplayPort.Catalog>(scope, ISnapshotReplayPort.Capability, null, ISnapshotReplayPort.List,
            (port, _) => port.ReadAsync(scope, token), token);

    internal ValueTask<AdapterResult<MutationReceipt>> ChangeSnapshotReplayAsync(MutationRequest<ISnapshotReplayPort.Change> request,
        CancellationToken token)
    {
        var operation = request.Payload switch
        {
            ISnapshotReplayPort.Change.SaveSnapshot save when !string.IsNullOrWhiteSpace(save.Label) && save.Label.Length <= 256 =>
                ISnapshotReplayPort.Save,
            ISnapshotReplayPort.Change.RestoreSnapshot restore when MachineMap.Identifier(restore.Id) => ISnapshotReplayPort.Restore,
            ISnapshotReplayPort.Change.SetRecording => ISnapshotReplayPort.Record,
            ISnapshotReplayPort.Change.OpenRecording open when ValidArtifact(open.Artifact) => ISnapshotReplayPort.Open,
            ISnapshotReplayPort.Change.SeekRecording seek when MachineMap.Identifier(seek.Id) && ValidMediaTime(seek.Position) => ISnapshotReplayPort.Seek,
            ISnapshotReplayPort.Change.RewindLive rewind when ValidMediaTime(rewind.Position) => ISnapshotReplayPort.Rewind,
            _ => null,
        };
        return operation is null ? InvalidPortInput<MutationReceipt>(request.Scope)
            : MutatePortAsync<ISnapshotReplayPort>(new(request.Scope, ISnapshotReplayPort.Capability, null, operation),
                (port, cancellation) => port.ChangeAsync(request, cancellation), null, token);
    }

    private static bool ValidCapture(IInputPort.Capture capture) =>
        MachineMap.Identifier(capture.DeviceId) && capture.CaptureId != Guid.Empty;

    private static bool ValidInput(IInputPort.InputEvent input) => input switch
    {
        IInputPort.InputEvent.Key key => MachineMap.Identifier(key.Code),
        IInputPort.InputEvent.Pointer => true,
        IInputPort.InputEvent.Controller controller => controller.Axes is not null &&
            controller.Axes.Count <= 32 && controller.Axes.Keys.All(MachineMap.Identifier),
        _ => false,
    };

    private static bool ValidArtifact(ArtifactReference artifact) => MachineMap.Identifier(artifact.Id) && Enum.IsDefined(artifact.Source);

    private static bool ValidMediaTime(MediaTimestamp time) => time.TicksPerSecond > 0 && MachineMap.Identifier(time.ClockDomain);

    private static ValueTask<AdapterError?> CheckAdvertisedLimit(CapabilityDescriptor capability, SessionOperation scope,
        string key, ulong desired, string description) => ValueTask.FromResult<AdapterError?>(
        capability.Limits.TryGetValue(key, out var max) && desired > max ? PortLimit(scope, description) : null);
}
