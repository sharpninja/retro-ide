using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class AdapterConformanceTests
{
    /// <summary>Checks capability eligibility, exact scope and revision, and host actor permissions.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task Execution_routes_only_advertised_scoped_operations_and_preserves_host_denial()
    {
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var command = new ExecutionCommand(ExecutionAction.Pause);
        var scope = adapter.Scope("monitor");

        var wrongInstance = scope with { Session = scope.Session with { InstanceId = Guid.NewGuid() } };
        await RejectAsync(wrongInstance, command, AdapterErrorCodes.Incompatible);
        await RejectAsync(scope with { ExpectedRevision = 0 }, command, AdapterErrorCodes.RevisionConflict);
        await RejectAsync(scope, new ExecutionCommand(ExecutionAction.StepPhase), AdapterErrorCodes.Unsupported);
        await RejectAsync(scope, new ExecutionCommand(ExecutionAction.StepInstruction, "cpu", 3), AdapterErrorCodes.InvalidArgument);
        Assert.Empty(adapter.Dispatched);

        await RejectAsync(scope, command, AdapterErrorCodes.PermissionDenied);
        Assert.Equal(scope, Assert.Single(adapter.Dispatched).Scope);
        var accepted = adapter.Scope("desktop");
        var result = await control.ExecuteAsync(new(accepted, command), TestContext.Current.CancellationToken);
        Assert.Equal(new MutationReceipt(accepted, 2), result.Value);
        Assert.Equal(ExecutionStatus.Paused, adapter.Current.Execution);

        async Task RejectAsync(SessionOperation candidate, ExecutionCommand action, string code)
        {
            var rejection = await control.ExecuteAsync(new(candidate, action), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.False(rejection.Succeeded);
            Assert.Equal(code, rejection.Error.Code, StringComparer.Ordinal);
            Assert.Equal(candidate.OperationId, rejection.Error.OperationId);
        }
    }

    /// <summary>Checks receipt-based recovery after a host reply is lost, including a confirmed non-application.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Theory]
    [InlineData(MutationDisposition.Applied)]
    [InlineData(MutationDisposition.NotApplied)]
    public async Task An_uncertain_execution_requires_authoritative_reconciliation_before_retry(MutationDisposition disposition)
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var scope = adapter.Scope("desktop");
        var request = new MutationRequest<ExecutionCommand>(scope, new(ExecutionAction.StepInstruction, "cpu"));
        adapter.OnExecute = (command, _) =>
        {
            if (disposition == MutationDisposition.Applied)
                adapter.Apply(command);
            return ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.Timeout, "Host reply lost.")));
        };

        Assert.Equal(AdapterErrorCodes.UnknownOutcome, (await control.ExecuteAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        Assert.Equal(AdapterErrorCodes.UnknownOutcome, (await control.ExecuteAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        Assert.Equal(AdapterErrorCodes.PermissionDenied,
            (await control.ReconcileAsync(scope with { ActorId = "different-client" }, token)).Error?.Code, StringComparer.Ordinal);
        Assert.Empty(adapter.Checked);
        Assert.Single(adapter.Dispatched);

        adapter.Disposition = MutationDisposition.Unknown;
        Assert.Equal(MutationDisposition.Unknown, (await control.ReconcileAsync(scope, token)).Value?.Disposition);
        Assert.Equal(AdapterErrorCodes.UnknownOutcome, (await control.ExecuteAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        adapter.Disposition = disposition;
        Assert.Equal(disposition, (await control.ReconcileAsync(scope, token)).Value?.Disposition);
        Assert.All(adapter.Checked, checkedScope => Assert.Equal(scope, checkedScope));
        Assert.Single(adapter.Dispatched);

        adapter.OnExecute = null;
        var changedRequest = request with { Payload = request.Payload with { Count = 2 } };
        var changed = await control.ExecuteAsync(changedRequest, token);
        Assert.False(changed.Succeeded);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, changed.Error.Code, StringComparer.Ordinal);
        Assert.Equal(scope.OperationId, changed.Error.OperationId);

        var retry = await control.ExecuteAsync(request, token);
        if (disposition == MutationDisposition.Applied)
        {
            Assert.Equal(new MutationReceipt(scope, 2), retry.Value);
            Assert.Single(adapter.Dispatched);
        }
        else
        {
            Assert.True(retry.Succeeded);
            Assert.Equal(2, adapter.Dispatched.Count);
        }
    }

    /// <summary>Keeps the first operation tied to its payload after an unrelated operation is reconciled.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task Earlier_reconciled_operations_keep_their_payload_bindings()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter { Disposition = MutationDisposition.NotApplied };
        var control = adapter.StartControl();
        var first = new MutationRequest<ExecutionCommand>(adapter.Scope("desktop"), new(ExecutionAction.StepInstruction, "cpu"));
        var second = new MutationRequest<ExecutionCommand>(adapter.Scope("desktop"), new(ExecutionAction.Pause));
        adapter.OnExecute = (_, _) => ValueTask.FromResult(AdapterResult<MutationReceipt>.Failure(new(AdapterErrorCodes.Timeout, "Host reply lost.")));
        foreach (var request in new[] { first, second })
        {
            var unknown = await control.ExecuteAsync(request, token);
            Assert.Equal(AdapterErrorCodes.UnknownOutcome, unknown.Error?.Code, StringComparer.Ordinal);
            Assert.Equal(MutationDisposition.NotApplied, (await control.ReconcileAsync(request.Scope, token)).Value?.Disposition);
        }

        adapter.OnExecute = null;
        var changed = await control.ExecuteAsync(first with { Payload = first.Payload with { Count = 2 } }, token);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, changed.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(first.Scope.OperationId, changed.Error?.OperationId);
        Assert.Equal([first, second], adapter.Dispatched);

        var retry = await control.ExecuteAsync(first, token);
        Assert.Equal(new MutationReceipt(first.Scope, 2), retry.Value);
        Assert.Equal(retry.Value, (await control.ExecuteAsync(first, token)).Value);
        Assert.Equal([first, second, first], adapter.Dispatched);
    }

    /// <summary>Separates a cancelled local request from a dispatched command with an unknown outcome.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task Command_cancellation_distinguishes_pre_dispatch_from_a_lost_reply()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter { Disposition = MutationDisposition.Applied };
        var control = adapter.StartControl();
        var request = new MutationRequest<ExecutionCommand>(adapter.Scope("desktop"), new(ExecutionAction.Pause));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancelled.Cancel();

        var local = await control.ExecuteAsync(request, cancelled.Token);
        Assert.Equal(AdapterErrorCodes.Cancelled, local.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(request.Scope.OperationId, local.Error?.OperationId);
        Assert.Empty(adapter.Dispatched);

        using var live = CancellationTokenSource.CreateLinkedTokenSource(token);
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnExecute = async (operation, cancellation) =>
        {
            var applied = adapter.Apply(operation);
            dispatched.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
            return applied;
        };
        var pending = control.ExecuteAsync(request, live.Token).AsTask();
        await dispatched.Task.WaitAsync(token);
        live.Cancel();

        var lost = await pending;
        Assert.Equal(AdapterErrorCodes.UnknownOutcome, lost.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(request.Scope.OperationId, lost.Error?.OperationId);
        Assert.Equal(MutationDisposition.Applied, (await control.ReconcileAsync(request.Scope, token)).Value?.Disposition);
        Assert.Equal(new MutationReceipt(request.Scope, 2), (await control.ExecuteAsync(request, token)).Value);
        Assert.Single(adapter.Dispatched);
    }

    /// <summary>Checks that a concurrent command rechecks the host revision after the preceding command finishes.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics.</remarks>
    [Fact]
    public async Task Conflicting_execution_requests_are_serialized_and_recheck_the_state_revision()
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new ControlAdapter();
        var control = adapter.StartControl();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnExecute = async (request, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return adapter.Apply(request);
        };
        var first = control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Pause)), token).AsTask();
        await entered.Task.WaitAsync(token);

        var second = control.ExecuteAsync(new(adapter.Scope("desktop"), new(ExecutionAction.Run)), token).AsTask();
        bool dispatchedEarly;
        try { dispatchedEarly = second.IsCompleted || adapter.Dispatched.Count != 1; }
        finally { release.TrySetResult(); }

        Assert.False(dispatchedEarly);
        Assert.True((await first).Succeeded);
        Assert.Equal(AdapterErrorCodes.RevisionConflict, (await second).Error?.Code, StringComparer.Ordinal);
        Assert.Single(adapter.Dispatched);
    }
}
