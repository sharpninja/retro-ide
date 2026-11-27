using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    /// <summary>Checks both explicit expiration and a sequence jump, followed by state reload and cursor resumption.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Event_reader_reloads_authoritative_state_after_a_gap_and_then_resumes(bool explicitGap)
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var hydrated = adapter.MakeState(5, 4, SessionLifecycle.Connected, includeExecution: false);
        adapter.Current = hydrated;
        adapter.Events[1] = [explicitGap ? new SessionEventGap(new(adapter.Identity, 4), 3) :
            new FutureEvent(new(adapter.Identity, 4), 3)];
        adapter.Events[5] = [new FutureEvent(new(adapter.Identity, 6), 4)];

        var events = await control.WatchAsync(null, token).ToArrayAsync(token);

        Assert.Collection(events,
            item => Assert.Equal(4UL, Assert.IsType<SessionEventGap>(item.Value).Cursor.Sequence),
            item => Assert.Equal(hydrated, Assert.IsType<SessionStateChanged>(item.Value).State),
            item => Assert.Equal(6UL, Assert.IsType<FutureEvent>(item.Value).Cursor.Sequence));
        Assert.Equal([1UL, 5UL], adapter.Subscribed);
        Assert.Equal(1, adapter.StateReads);
        Assert.Equal(hydrated, control.State);
        Assert.Equal(AdapterErrorCodes.Unsupported,
            (await control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), token)).Error?.Code, StringComparer.Ordinal);
    }

    /// <summary>Checks validation of event ownership and recovery of the same session after disconnect.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task A_disconnected_session_rejects_mutations_and_resumes_from_its_own_cursor()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var foreign = new EventCursor(adapter.Identity with { InstanceId = Guid.NewGuid() }, 1);
        var rejected = await control.WatchAsync(foreign, token).ToArrayAsync(token);
        Assert.Equal(AdapterErrorCodes.Incompatible, Assert.Single(rejected).Error?.Code, StringComparer.Ordinal);
        Assert.Empty(adapter.Subscribed);

        adapter.Current = adapter.MakeState(2, 2, SessionLifecycle.Disconnected);
        adapter.Events[1] = [new SessionStateChanged(adapter.Current)];
        Assert.True(Assert.Single(await control.WatchAsync(null, token).ToArrayAsync(token)).Succeeded);
        Assert.Equal(SessionLifecycle.Disconnected, control.State.Lifecycle);
        Assert.Equal(AdapterErrorCodes.Disconnected,
            (await control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), token)).Error?.Code, StringComparer.Ordinal);
        Assert.Empty(adapter.Dispatched);

        var connected = adapter.MakeState(3, 3, SessionLifecycle.Connected);
        adapter.Current = connected;
        adapter.Events[2] = [new SessionStateChanged(connected)];
        var resumed = Assert.Single(await control.WatchAsync(control.State.Cursor, token).ToArrayAsync(token));
        Assert.Equal(connected, Assert.IsType<SessionStateChanged>(resumed.Value).State);
        Assert.Equal([1UL, 2UL], adapter.Subscribed);
        Assert.True((await control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), token)).Succeeded);
    }

    /// <summary>Prevents an in-flight state read from routing against a revision the event stream has superseded.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Theory]
    [InlineData(true, AdapterErrorCodes.Unsupported)]
    [InlineData(false, AdapterErrorCodes.RevisionConflict)]
    public async Task A_late_state_reply_cannot_restore_superseded_capabilities_or_revisions(bool withdrawn, string error)
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var stale = adapter.Current;
        var latest = adapter.MakeState(2, 2, SessionLifecycle.Connected, includeExecution: !withdrawn);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<AdapterResult<SessionState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnReadState = cancellation =>
        {
            reading.SetResult();
            return new(reply.Task.WaitAsync(cancellation));
        };
        var scope = adapter.Scope("desktop") with { ExpectedRevision = withdrawn ? latest.StateRevision : stale.StateRevision };
        var operation = control.ExecuteAsync(new(scope, new(ExecutionAction.Pause)), token).AsTask();
        try
        {
            await reading.Task.WaitAsync(token);
            adapter.Current = latest;
            adapter.Events[stale.Cursor.Sequence] = [new SessionStateChanged(latest)];
            Assert.Equal(latest, Assert.IsType<SessionStateChanged>(Assert.Single(await control.WatchAsync(stale.Cursor, token)
                .ToArrayAsync(token)).Value).State);
        }
        finally
        {
            reply.TrySetResult(AdapterResult<SessionState>.Success(stale));
        }

        var result = await operation;
        Assert.Equal(error, result.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(scope.OperationId, result.Error?.OperationId);
        Assert.Empty(adapter.Dispatched);
        Assert.Equal(latest, control.State);
    }

    private sealed record FutureEvent(EventCursor Cursor, ulong StateRevision) : SessionEvent(Cursor, StateRevision);
}
