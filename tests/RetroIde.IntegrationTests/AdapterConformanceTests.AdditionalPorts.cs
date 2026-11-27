using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    /// <summary>Checks typed debugger, optional live timing, and register query and mutation routing.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    [Fact]
    public async Task Timing_registers_and_stop_rules_use_the_negotiated_cpu_subjects()
    {
        var adapter = new ControlAdapter();
        adapter.EnableAdditionalPorts();
        var control = adapter.StartControl();
        var cancellation = TestContext.Current.CancellationToken;
        var clock = new ITimingPort.Clock("cpu.clock");
        var timing = await control.ObserveTimingAsync(new(adapter.Scope("desktop"), clock), cancellation).ConfigureAwait(true);
        Assert.Equal("phase-1", timing.Value?.Phase, StringComparer.Ordinal);
        Assert.True((await control.SetThrottleAsync(new(adapter.Scope("desktop"), new(clock, "fixed", 1.5m)), cancellation).ConfigureAwait(true)).Succeeded);
        timing = await control.ObserveTimingAsync(new(adapter.Scope("desktop"), clock), cancellation).ConfigureAwait(true);
        Assert.Equal(1.5m, timing.Value?.SpeedRatio);
        var denied = await control.ObserveTimingAsync(new(adapter.Scope("desktop"), new("unknown")), cancellation).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.Unsupported, denied.Error?.Code, StringComparer.Ordinal);
        var bus = await control.ReadBusAsync(new(adapter.Scope("desktop"), new(clock, 2, 1)), cancellation).ConfigureAwait(true);
        Assert.Equal(ITimingPort.BusCycle.Read, Assert.Single(bus.Value!.Items).Cycle);

        var values = await control.ReadRegistersAsync(new(adapter.Scope("desktop"), new("cpu", ["D0"])), cancellation).ConfigureAwait(true);
        Assert.Equal(32, Assert.Single(values.Value!.Values).WidthBits);
        var changed = values.Value.Values[0] with { Value = [0, 0, 0, 0x40] };
        Assert.True((await control.WriteRegistersAsync(new(adapter.Scope("desktop"), new("cpu", [changed])), cancellation).ConfigureAwait(true)).Succeeded);
        values = await control.ReadRegistersAsync(new(adapter.Scope("desktop"), new("cpu", [])), cancellation).ConfigureAwait(true);
        Assert.Equal(changed, Assert.Single(values.Value!.Values));

        var address = new MemoryAddress("main", 0x100, "cpu", MapRevision: 8);
        var rule = new IDebuggerPort.Rule("watch", address, 4, IDebuggerPort.Trigger.Write, true);
        Assert.True((await control.ChangeStopRuleAsync(new(adapter.Scope("desktop"), new(IDebuggerPort.RuleAction.Put, rule)), cancellation)
            .ConfigureAwait(true)).Succeeded);
        var rules = await control.ListStopRulesAsync(new(adapter.Scope("desktop"), new("cpu")), cancellation).ConfigureAwait(true);
        Assert.Equal(rule, Assert.Single(rules.Value!.Items));
        Assert.True((await control.ChangeStopRuleAsync(new(adapter.Scope("desktop"), new(IDebuggerPort.RuleAction.Remove, rule)), cancellation)
            .ConfigureAwait(true)).Succeeded);
        rules = await control.ListStopRulesAsync(new(adapter.Scope("desktop"), new("cpu")), cancellation).ConfigureAwait(true);
        Assert.Empty(rules.Value!.Items);
    }

    /// <summary>Checks native disassembly and trace routing while rejecting stale or out-of-range inspection.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    [Fact]
    public async Task Native_disassembly_uses_the_current_mapping_and_trace_keeps_the_cpu_scope()
    {
        var adapter = new ControlAdapter();
        adapter.EnableAdditionalPorts();
        var control = adapter.StartControl();
        var token = TestContext.Current.CancellationToken;
        var scope = adapter.Scope("desktop");
        var start = new MemoryAddress("main", 0x200, "cpu", MapRevision: 8);

        var decoded = await control.DisassembleAsync(new(scope, new(start, 1)), token).ConfigureAwait(true);
        Assert.Equal("nop", Assert.Single(decoded.Value!.Items).Mnemonic, StringComparer.Ordinal);
        var stale = await control.DisassembleAsync(new(scope, new(start with { MapRevision = 7 }, 1)), token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.RevisionConflict, stale.Error?.Code, StringComparer.Ordinal);
        var boundary = await control.DisassembleAsync(new(scope, new(start with { Offset = 0x7FFF }, 1)), token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.Incompatible, boundary.Error?.Code, StringComparer.Ordinal);
        var trace = await control.ReadTraceAsync(new(scope, new("cpu", null, 1)), token).ConfigureAwait(true);
        Assert.Equal(start, Assert.Single(trace.Value!.Entries).Instruction.Address);
    }

    /// <summary>Checks ordered input and distinct mutation and enumeration paths for media and replay.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    [Fact]
    public async Task Input_artifact_mounts_and_replay_keep_their_own_scopes_and_capability_checks()
    {
        var adapter = new ControlAdapter();
        adapter.EnableAdditionalPorts();
        var control = adapter.StartControl();
        var cancellation = TestContext.Current.CancellationToken;
        var capture = new IInputPort.Capture("keyboard", Guid.NewGuid());
        var key = new IInputPort.InputEvent.Key("A", true);
        var scope = adapter.Scope("desktop") with { ExpectedRevision = null };
        var input = new MutationRequest<IInputPort.Batch>(scope, new(capture, 0, [key]));
        Assert.True((await control.SendInputAsync(input, cancellation).ConfigureAwait(true)).Succeeded);
        var gap = await control.SendInputAsync(new(scope with { OperationId = Guid.NewGuid() }, new(capture, 2, [key])), cancellation).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidState, gap.Error?.Code, StringComparer.Ordinal);
        var oversize = await control.SendInputAsync(new(scope with { OperationId = Guid.NewGuid() }, new(capture, 1, [key, key, key])),
            cancellation).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, oversize.Error?.Code, StringComparer.Ordinal);
        Assert.True((await control.ReleaseInputAsync(new(scope with { OperationId = Guid.NewGuid() }, capture), cancellation).ConfigureAwait(true)).Succeeded);

        var artifact = new ArtifactReference("fixture.disk", ArtifactReference.Origin.Uploaded);
        Assert.True((await control.ChangeMediaAsync(new(adapter.Scope("desktop"), new IMediaStoragePort.Change.Insert("drive-0", artifact, true)),
            cancellation).ConfigureAwait(true)).Succeeded);
        var slots = await control.ListMediaSlotsAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        Assert.Equal(artifact, Assert.Single(slots.Value!.Items).Mounted);
        Assert.True((await control.ChangeMediaAsync(new(adapter.Scope("desktop"), new IMediaStoragePort.Change.Remove("drive-0")), cancellation)
            .ConfigureAwait(true)).Succeeded);

        Assert.True((await control.ChangeSnapshotReplayAsync(new(adapter.Scope("desktop"), new ISnapshotReplayPort.Change.SaveSnapshot("Pause")),
            cancellation).ConfigureAwait(true)).Succeeded);
        var time = new MediaTimestamp("fixture.clock", 7, 1000);
        Assert.True((await control.ChangeSnapshotReplayAsync(new(adapter.Scope("desktop"), new ISnapshotReplayPort.Change.SeekRecording("fixture.replay", time)),
            cancellation).ConfigureAwait(true)).Succeeded);
        var catalog = await control.ReadSnapshotsAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        Assert.Equal(7UL, catalog.Value!.ActiveReplay?.Position);
        Assert.Equal("Pause", Assert.Single(catalog.Value.Snapshots).Label, StringComparer.Ordinal);
        var liveRewind = await control.ChangeSnapshotReplayAsync(new(adapter.Scope("desktop"), new ISnapshotReplayPort.Change.RewindLive(time)),
            cancellation).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.Unsupported, liveRewind.Error?.Code, StringComparer.Ordinal);
    }

    /// <summary>Checks scoped consoles, artifacts, fixture outcomes and guest services alongside an actual optional port.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    [Fact]
    public async Task Older_clients_keep_common_control_when_a_binding_implements_a_new_optional_port()
    {
        var adapter = new ControlAdapter();
        adapter.EnableAdditionalPorts();
        var control = adapter.StartControl();
        var cancellation = TestContext.Current.CancellationToken;
        var unknown = new CapabilityId("fixture.new-feature", new Version(3, 1));
        Assert.True(adapter.TryGetPort<IFutureAdapterPort>(unknown, out var future));
        Assert.Equal("future reply", future!.Probe(), StringComparer.Ordinal);
        adapter.PortRequests.Clear();

        var consoles = await control.ListConsolesAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        Assert.Equal([IConsolePort.Origin.Guest, IConsolePort.Origin.Backend], consoles.Value!.Items.Select(channel => channel.Source));
        var data = await control.ReadConsoleAsync(new(adapter.Scope("desktop"), new("serial", null, 5)), cancellation).ConfigureAwait(true);
        Assert.Equal(0x41, Assert.Single(Assert.Single(data.Value!.Items).Data));
        var artifacts = await control.ListDiagnosticsAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        Assert.Equal("fixture.log", Assert.Single(artifacts.Value!.Items).Reference.Id, StringComparer.Ordinal);

        var fixtures = await control.ListTestFixturesAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        var fixture = Assert.Single(fixtures.Value!.Items);
        var run = adapter.Scope("desktop");
        Assert.True((await control.RunGuestFixtureAsync(new(run, new(fixture.Id, 2)), cancellation).ConfigureAwait(true)).Succeeded);
        var report = await control.ReadTestReportAsync(new(adapter.Scope("desktop"), new(run.OperationId)), cancellation).ConfigureAwait(true);
        Assert.Equal(run.OperationId, report.Value!.Run.OperationId);
        Assert.True(Assert.Single(report.Value.Assertions).Passed);

        var guest = await control.ReadGuestServicesAsync(adapter.Scope("desktop"), cancellation).ConfigureAwait(true);
        Assert.Equal(Assert.Single(guest.Value!.Containers).Id, Assert.Single(guest.Value.Services).ContainerId, StringComparer.Ordinal);
        Assert.True((await control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), cancellation).ConfigureAwait(true)).Succeeded);
        Assert.DoesNotContain(unknown, adapter.PortRequests);
    }
}
