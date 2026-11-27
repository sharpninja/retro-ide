using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application.Emulators;
using RetroIde.Emulation;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    private sealed partial class ControlAdapter : IEmulatorSession, IExecutionPort, IOperationReceiptPort
    {
        private static readonly CapabilityDescriptor Receipts = new(IOperationReceiptPort.Capability, null,
            [IOperationReceiptPort.Reconcile], ImmutableDictionary<string, ulong>.Empty);
        private static readonly CapabilityDescriptor Execution = new(IExecutionPort.Capability, null,
            ["run", "pause"], ImmutableDictionary<string, ulong>.Empty);
        private static readonly CapabilityDescriptor Cpu = new(IExecutionPort.Capability, "cpu", ["step-instruction"],
            ImmutableDictionary<string, ulong>.Empty.Add("max-count", 2));
        private static readonly CapabilityDescriptor FutureCapability = new(new("fixture.new-feature", new Version(3, 1)), null,
            ["frob"], ImmutableDictionary<string, ulong>.Empty);

        internal ControlAdapter()
        {
            Current = MakeState(1, 1, SessionLifecycle.Connected);
        }

        public SessionIdentity Identity { get; } = new("fixture", "fixture", "1.0.0", Guid.NewGuid());
        internal SessionState Current { get; set; }
        internal List<MutationRequest<ExecutionCommand>> Dispatched { get; } = [];
        internal List<SessionOperation> Checked { get; } = [];
        internal Dictionary<ulong, SessionEvent[]> Events { get; } = [];
        internal List<ulong> Subscribed { get; } = [];
        internal int StateReads { get; private set; }
        internal MutationDisposition Disposition { get; set; }
        internal Func<MutationRequest<ExecutionCommand>, CancellationToken, ValueTask<AdapterResult<MutationReceipt>>>? OnExecute { get; set; }

        internal EmulatorSessionControl StartControl() => new(this, Current, NullLogger.Instance);

        internal SessionState MakeState(ulong cursor, ulong revision, SessionLifecycle lifecycle, bool includeExecution = true) =>
            new(Identity, lifecycle, ExecutionStatus.Running, revision, SessionClosePolicy.Detach,
                new CapabilitySnapshot(revision, includeExecution
                    ? [Execution, Cpu, Receipts, Video, Audio, FutureCapability] : [Receipts, Video, Audio, FutureCapability]),
                new EventCursor(Identity, cursor));

        internal SessionOperation Scope(string actor) => new(Identity, Guid.NewGuid(), actor, Current.StateRevision);

        public ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken)
        {
            StateReads++;
            return ValueTask.FromResult(AdapterResult<SessionState>.Success(Current));
        }

        public IAsyncEnumerable<SessionEvent> ReadEventsAsync(EventCursor? after, CancellationToken cancellationToken)
        {
            var sequence = after?.Sequence ?? 0;
            Subscribed.Add(sequence);
            return Events.TryGetValue(sequence, out var events) ? events.ToAsyncEnumerable() : AsyncEnumerable.Empty<SessionEvent>();
        }

        public bool TryGetPort<TPort>(CapabilityId capability, [NotNullWhen(true)] out TPort? port) where TPort : class, IEmulatorPort
        {
            PortRequests.Add(capability);
            port = capability == IExecutionPort.Capability || capability == IOperationReceiptPort.Capability ||
                capability == IVideoPort.Capability || capability == IAudioPort.Capability || capability == FutureCapability.Id ||
                AdditionalCapabilities.Any(item => item.Id == capability) ? this as TPort : null;
            return port is not null;
        }

        public ValueTask<AdapterResult<MutationReceipt>> ExecuteAsync(MutationRequest<ExecutionCommand> request, CancellationToken cancellationToken)
        {
            Dispatched.Add(request);
            return OnExecute is { } custom ? custom(request, cancellationToken) : ValueTask.FromResult(Apply(request));
        }

        internal AdapterResult<MutationReceipt> Apply(MutationRequest<ExecutionCommand> request)
        {
            var scope = request.Scope;
            if (!string.Equals(scope.ActorId, "desktop", StringComparison.Ordinal))
                return AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.PermissionDenied, "Actor denied by host.", scope.OperationId));
            if (scope.ExpectedRevision != Current.StateRevision)
                return AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.RevisionConflict, "State revision conflict.", scope.OperationId, Current.StateRevision));

            Current = Current with
            {
                StateRevision = Current.StateRevision + 1,
                Execution = request.Payload.Action == ExecutionAction.Run ? ExecutionStatus.Running : ExecutionStatus.Paused,
                Cursor = new EventCursor(Identity, Current.Cursor.Sequence + 1),
            };
            return AdapterResult<MutationReceipt>.Success(new(scope, Current.StateRevision));
        }

        public ValueTask<AdapterResult<MutationResolution>> ReconcileAsync(SessionOperation operation, CancellationToken cancellationToken)
        {
            Checked.Add(operation);
            return ValueTask.FromResult(AdapterResult<MutationResolution>.Success(new(operation, Disposition, Current.StateRevision)));
        }

        public ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(SessionCloseRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<SessionCloseResult>.Success(new(request.OperationId, SessionClosePolicy.Detach)));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
