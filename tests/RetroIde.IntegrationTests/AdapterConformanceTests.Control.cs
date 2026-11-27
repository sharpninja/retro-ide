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
