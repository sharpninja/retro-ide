using System.Collections.Immutable;
using RetroIde.Emulation;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    private sealed partial class ControlAdapter : ISnapshotReplayPort, IConsolePort, ITestAutomationPort, IGuestServicesPort
    {
        private static readonly ImmutableArray<CapabilityDescriptor> ServiceCapabilities =
        [
            Advertise(ISnapshotReplayPort.Capability, null, [ISnapshotReplayPort.List, ISnapshotReplayPort.Save, ISnapshotReplayPort.Seek]),
            Advertise(IConsolePort.Capability, null, [IConsolePort.List, IConsolePort.ListArtifacts]),
            Advertise(IConsolePort.Capability, "serial", [IConsolePort.Read], Limit("max-records", 5)),
            Advertise(ITestAutomationPort.Capability, null, [ITestAutomationPort.List, ITestAutomationPort.ReadResult]),
            Advertise(ITestAutomationPort.Capability, "boot-test", [ITestAutomationPort.Run], Limit("max-iterations", 2)),
            Advertise(IGuestServicesPort.Capability, null, [IGuestServicesPort.List]),
        ];

        private static readonly MediaTimestamp ReplayStart = new("fixture.clock", 0, 1000);
        private ISnapshotReplayPort.Replay _replay = new("fixture.replay", ReplayStart, 100, 0, true);
        private ISnapshotReplayPort.Snapshot? _snapshot;
        private Guid _fixtureRun;

        ValueTask<AdapterResult<ISnapshotReplayPort.Catalog>> ISnapshotReplayPort.ReadAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new ISnapshotReplayPort.Catalog(_snapshot is null ? [] : [_snapshot], _replay));

        ValueTask<AdapterResult<MutationReceipt>> ISnapshotReplayPort.ChangeAsync(MutationRequest<ISnapshotReplayPort.Change> request,
            CancellationToken cancellationToken) => request.Payload switch
            {
                ISnapshotReplayPort.Change.SaveSnapshot snapshot => ApplyPort(request.Scope, () =>
                    _snapshot = new("paused", snapshot.Label, "fixture.abi", DateTimeOffset.UnixEpoch, Disk)),
                ISnapshotReplayPort.Change.SeekRecording seek when string.Equals(seek.Id, _replay.Id, StringComparison.Ordinal) &&
                    seek.Position.Ticks <= _replay.EndTick => ApplyPort(request.Scope, () => _replay = _replay with { Position = seek.Position.Ticks }),
                _ => Unsupported<MutationReceipt>(),
            };

        ValueTask<AdapterResult<IConsolePort.Channels>> IConsolePort.ListAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new IConsolePort.Channels([new("serial", IConsolePort.Origin.Guest, "UTF-8", false),
                new("host", IConsolePort.Origin.Backend, "UTF-8", false)]));

        ValueTask<AdapterResult<IConsolePort.Records>> IConsolePort.ReadAsync(ScopedQuery<IConsolePort.ChannelRead> request, CancellationToken cancellationToken) =>
            Success(new IConsolePort.Records(request.Payload.ChannelId, false, [new(1, ReplayStart, null, [0x41])]));

        IAsyncEnumerable<IConsolePort.Records> IConsolePort.ObserveAsync(ScopedQuery<IConsolePort.ChannelRead> request,
            CancellationToken cancellationToken) => AsyncEnumerable.Empty<IConsolePort.Records>();

        ValueTask<AdapterResult<MutationReceipt>> IConsolePort.SendAsync(MutationRequest<IConsolePort.ChannelInput> request,
            CancellationToken cancellationToken) => Unsupported<MutationReceipt>();

        ValueTask<AdapterResult<IConsolePort.Artifacts>> IConsolePort.ListArtifactsAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new IConsolePort.Artifacts([new(new("fixture.log", ArtifactReference.Origin.Backend), "fixture.log", "text/plain", 1)]));

        ValueTask<AdapterResult<ITestAutomationPort.Fixtures>> ITestAutomationPort.ListAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new ITestAutomationPort.Fixtures([new("boot-test", "Boot fixture", 2)]));

        ValueTask<AdapterResult<MutationReceipt>> ITestAutomationPort.RunAsync(MutationRequest<ITestAutomationPort.RunRequest> request,
            CancellationToken cancellationToken) => ApplyPort(request.Scope, () => _fixtureRun = request.Scope.OperationId);

        ValueTask<AdapterResult<ITestAutomationPort.Report>> ITestAutomationPort.ReadAsync(ScopedQuery<ITestAutomationPort.RunIdentity> request,
            CancellationToken cancellationToken) => request.Payload.OperationId == _fixtureRun
            ? Success(new ITestAutomationPort.Report(request.Payload, true, [new("booted", true, "Fixture is ready.")], []))
            : Unsupported<ITestAutomationPort.Report>();

        ValueTask<AdapterResult<IGuestServicesPort.Snapshot>> IGuestServicesPort.ReadAsync(SessionOperation scope, CancellationToken cancellationToken) =>
            Success(new IGuestServicesPort.Snapshot([new("guest-fs", "storage", "ready", "container-a")],
                [new("container-a", "boot", "fixture", "running")]));
    }
}
