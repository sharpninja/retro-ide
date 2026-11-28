using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RetroIde.Application.Emulators;
using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TEST-RETRO-010#ac-test-session-contract; TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task A_fresh_pinned_attachment_reconciles_the_original_hosts_operation_without_redispatch()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        var (workspace, project, installation) = await PrepareSelectedBindingAsync(root);
        var identity = new SessionIdentity(installation.Manifest.AdapterId, FirstPin.Bundle, FirstPin.Version, Guid.NewGuid());
        var host = new AttachedHost(identity);
        var target = new EmulatorSessionPlan("fixture-profile", new EmulatorConnection.Attach("fixture.endpoint", identity));
        var first = (await workspace.OpenSelectedAsync(project, target, _ => host, token)).Value!;
        var scope = new SessionOperation(identity, Guid.NewGuid(), "desktop", first.InitialState.StateRevision);
        var request = new MutationRequest<ExecutionCommand>(scope, new(ExecutionAction.Pause));
        await using (first.ConfigureAwait(true))
        {
            host.DropNextReply = true;
            Assert.Equal(AdapterErrorCodes.UnknownOutcome, (await first.Control.ExecuteAsync(request, token)).Error?.Code,
                StringComparer.Ordinal);
        }
        Assert.True(Assert.IsType<AttachedConnection>(first.Connection).Disposed);

        var reopened = (await workspace.OpenSelectedAsync(project, target, _ => host, token)).Value!;
        await using (reopened.ConfigureAwait(true))
        {
            Assert.Equal(identity, reopened.InitialState.Identity);
            Assert.NotSame(first.Connection, reopened.Connection);
            var denied = await reopened.Control.ReconcileAsync(scope with { ActorId = "observer" }, token);
            Assert.Equal(AdapterErrorCodes.PermissionDenied, denied.Error?.Code, StringComparer.Ordinal);
            var resolved = await reopened.Control.ReconcileAsync(scope, token);
            Assert.Equal(new MutationResolution(scope, MutationDisposition.Applied, 2), resolved.Value);

            var backend = Assert.IsAssignableFrom<IExecutionPort>(reopened.Connection);
            var different = await backend.ExecuteAsync(request with { Payload = new(ExecutionAction.Run) }, token);
            Assert.Equal(AdapterErrorCodes.InvalidArgument, different.Error?.Code, StringComparer.Ordinal);
            Assert.Equal(new MutationReceipt(scope, 2), (await backend.ExecuteAsync(request, token)).Value);
            Assert.Equal(1, host.Applied);
        }
    }

    /// <remarks>TEST-RETRO-010#ac-test-media-ports; TEST-RETRO-010#ac-test-session-contract.</remarks>
    [Fact]
    public async Task Disposing_a_pinned_session_releases_active_control_and_video_subscriptions()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        var (workspace, project, installation) = await PrepareSelectedBindingAsync(root);
        var identity = new SessionIdentity(installation.Manifest.AdapterId, FirstPin.Bundle, FirstPin.Version, Guid.NewGuid());
        var host = new AttachedHost(identity);
        var target = new EmulatorSessionPlan("fixture-profile", new EmulatorConnection.Attach("fixture.endpoint", identity));
        await using var session = (await workspace.OpenSelectedAsync(project, target, _ => host, token)).Value!;
        var connection = Assert.IsType<AttachedConnection>(session.Connection);
        using var consumers = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var scope = new SessionOperation(identity, Guid.NewGuid(), "desktop", null);
        await using var events = session.Control.WatchAsync(null, consumers.Token).GetAsyncEnumerator(consumers.Token);
        await using var video = session.Control.WatchVideoAsync(new(scope, "display"), consumers.Token).GetAsyncEnumerator(consumers.Token);
        var eventRead = events.MoveNextAsync().AsTask();
        var videoRead = video.MoveNextAsync().AsTask();
        try
        {
            await Task.WhenAll(connection.EventsStarted.Task, connection.Video.Started.Task).WaitAsync(deadline.Token);
            Assert.Equal(EmulatorBundleStore.Removal.InUse, root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
            await session.DisposeAsync();
            if (await eventRead.WaitAsync(deadline.Token))
                Assert.Equal(AdapterErrorCodes.Cancelled, events.Current.Error?.Code, StringComparer.Ordinal);
            await AwaitStoppedMediaAsync(videoRead, deadline.Token);
            Assert.True(connection.Disposed);
            Assert.True(connection.EventsEnded);
            Assert.True(connection.Video.Ended);
            Assert.True(connection.Video.Disposed);
            Assert.Equal(EmulatorBundleStore.Removal.Removed, root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
        }
        finally
        {
            await consumers.CancelAsync();
            await eventRead;
            await AwaitStoppedMediaAsync(videoRead, token);
        }
    }

    private static async Task AwaitStoppedMediaAsync(Task<bool> read, CancellationToken deadline)
    {
        try { Assert.False(await read.WaitAsync(deadline)); }
        catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
    }

    private sealed class AttachedHost(SessionIdentity identity) : IEmulatorAdapterFactory
    {
        private static readonly ImmutableDictionary<string, ulong> Limits = ImmutableDictionary<string, ulong>.Empty;
        private static readonly CapabilitySnapshot Capabilities = new(1,
        [
            new(IExecutionPort.Capability, null, ["pause", "run"], Limits),
            new(IOperationReceiptPort.Capability, null, [IOperationReceiptPort.Reconcile], Limits),
            new(IVideoPort.Capability, "display", [IVideoPort.Subscribe], Limits),
        ]);
        private readonly Dictionary<Guid, AppliedOperation> _history = [];
        private ulong _revision = 1;
        private ExecutionStatus _status = ExecutionStatus.Running;

        internal SessionIdentity Identity { get; } = identity;
        internal bool DropNextReply { get; set; }
        internal int Applied => _history.Count;

        public ValueTask<AdapterResult<AdapterDescriptor>> DescribeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<AdapterDescriptor>.Success(new(Identity.AdapterId, [AdapterApi], [SessionMode.Attach], ["68000"])));

        public ValueTask<AdapterResult<IEmulatorSession>> OpenAsync(SessionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<IEmulatorSession>.Success(new AttachedConnection(this)));

        internal SessionState Read() =>
            new(Identity, SessionLifecycle.Connected, _status, _revision, SessionClosePolicy.Detach, Capabilities, new(Identity, _revision));

        internal AdapterResult<MutationReceipt> Execute(MutationRequest<ExecutionCommand> request)
        {
            var scope = request.Scope;
            if (_history.TryGetValue(scope.OperationId, out var previous))
            {
                if (previous.Request.Scope != scope)
                    return Reject<MutationReceipt>(AdapterErrorCodes.PermissionDenied, scope);
                return previous.Request.Payload == request.Payload
                    ? AdapterResult<MutationReceipt>.Success(previous.Receipt)
                    : Reject<MutationReceipt>(AdapterErrorCodes.InvalidArgument, scope);
            }
            if (scope.Session != Identity || !string.Equals(scope.ActorId, "desktop", StringComparison.Ordinal))
                return Reject<MutationReceipt>(AdapterErrorCodes.PermissionDenied, scope);
            if (scope.ExpectedRevision != _revision)
                return Reject<MutationReceipt>(AdapterErrorCodes.RevisionConflict, scope);

            _status = request.Payload.Action == ExecutionAction.Pause ? ExecutionStatus.Paused : ExecutionStatus.Running;
            var receipt = new MutationReceipt(scope, ++_revision);
            _history.Add(scope.OperationId, new(request, receipt));
            if (!DropNextReply)
                return AdapterResult<MutationReceipt>.Success(receipt);
            DropNextReply = false;
            return Reject<MutationReceipt>(AdapterErrorCodes.Timeout, scope);
        }

        internal AdapterResult<MutationResolution> Reconcile(SessionOperation scope)
        {
            if (!_history.TryGetValue(scope.OperationId, out var previous))
                return Reject<MutationResolution>(AdapterErrorCodes.Unavailable, scope);
            return previous.Request.Scope == scope
                ? AdapterResult<MutationResolution>.Success(new(scope, MutationDisposition.Applied, previous.Receipt.StateRevision))
                : Reject<MutationResolution>(AdapterErrorCodes.PermissionDenied, scope);
        }

        private static AdapterResult<T> Reject<T>(string code, SessionOperation scope) where T : class =>
            AdapterResult<T>.Failure(new(code, "Fixture operation rejected.", scope.OperationId));

        private sealed record AppliedOperation(MutationRequest<ExecutionCommand> Request, MutationReceipt Receipt);
    }

    private sealed class AttachedConnection(AttachedHost host) : IEmulatorSession, IExecutionPort, IOperationReceiptPort, IVideoPort
    {
        public SessionIdentity Identity => host.Identity;
        internal bool Disposed { get; private set; }
        internal TaskCompletionSource EventsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool EventsEnded { get; private set; }
        internal WaitingVideo Video { get; } = new(host.Identity);

        public ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<SessionState>.Success(host.Read()));

        public async IAsyncEnumerable<SessionEvent> ReadEventsAsync(EventCursor? after, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                EventsStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                yield break;
            }
            finally { EventsEnded = true; }
        }

        public bool TryGetPort<TPort>(CapabilityId capability, [NotNullWhen(true)] out TPort? port) where TPort : class, IEmulatorPort
        {
            port = capability == IExecutionPort.Capability || capability == IOperationReceiptPort.Capability || capability == IVideoPort.Capability
                ? this as TPort : null;
            return port is not null;
        }

        public ValueTask<AdapterResult<MutationReceipt>> ExecuteAsync(MutationRequest<ExecutionCommand> request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(host.Execute(request));

        public ValueTask<AdapterResult<MutationResolution>> ReconcileAsync(SessionOperation operation, CancellationToken cancellationToken) =>
            ValueTask.FromResult(host.Reconcile(operation));

        public ValueTask<AdapterResult<MediaPacket<VideoFormat>>> CaptureAsync(MediaStreamRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<MediaPacket<VideoFormat>>.Failure(new(AdapterErrorCodes.Unsupported, "Subscribe to the fixture feed.")));

        public ValueTask<AdapterResult<IEmulatorMediaStream<VideoFormat>>> SubscribeAsync(MediaStreamRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<IEmulatorMediaStream<VideoFormat>>.Success(Video));

        public ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(SessionCloseRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<SessionCloseResult>.Success(new(request.OperationId, SessionClosePolicy.Detach)));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WaitingVideo(SessionIdentity identity) : IEmulatorMediaStream<VideoFormat>
    {
        public SessionIdentity Session { get; } = identity;
        public Guid StreamId { get; } = Guid.NewGuid();
        public ulong FirstSequence => 1;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Ended { get; private set; }
        internal bool Disposed { get; private set; }

        public async IAsyncEnumerable<MediaPacket<VideoFormat>> ReadPacketsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                yield break;
            }
            finally { Ended = true; }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
