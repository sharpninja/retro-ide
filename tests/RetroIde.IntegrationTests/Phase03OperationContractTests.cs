using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Defines Phase 03's scoped operation lifecycle independently of the workbench implementation.</summary>
/// <remarks>2026-11-26. TEST-RETRO-015#ac-test-mcp-operation. Proves the Phase 03 handle and cancellation contract with a controlled port before binding the same assertions to the workbench. The later-phase Tasks and large-result scenarios are separate.</remarks>
public sealed partial class Phase03OperationContractTests
{
    /// <summary>Accepts the correct lifecycle and detects faults in identity, grant isolation, and terminal outcomes.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-015#ac-test-mcp-operation. Regenerates the chronological mock proof against the accepted Phase 02 baseline.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-handle")]
    [InlineData("foreign-status")]
    [InlineData("foreign-cancel")]
    [InlineData("premature-result")]
    [InlineData("wrong-final-state")]
    [InlineData("wrong-result")]
    public async Task Reference_detects_faults_in_the_scoped_operation_contract(string? fault)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reference = new ReferencePort(fault);
        if (fault is null)
        {
            await VerifyCancelledFetchAsync(reference, timeout.Token).ConfigureAwait(true);
            await VerifyCompletedFetchAsync(reference, timeout.Token).ConfigureAwait(true);
        }
        else
        {
            await Assert.ThrowsAnyAsync<XunitException>(
                () => VerifyCancelledFetchAsync(reference, timeout.Token)).ConfigureAwait(true);
        }
    }

    /// <summary>Detects loss of the original native failure or disclosure of its result to another grant.</summary>
    /// <remarks>TEST-RETRO-015#ac-test-mcp-operation; TEST-RETRO-015#ac-test-mcp-scope.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-failure-state")]
    [InlineData("wrong-native-failure")]
    [InlineData("foreign-result")]
    public async Task Reference_preserves_native_failure_and_hides_the_result_from_other_grants(string? fault)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reference = new ReferencePort(fault);
        if (fault is null)
            await VerifyFailedFetchAsync(reference, timeout.Token).ConfigureAwait(true);
        else
            await Assert.ThrowsAnyAsync<XunitException>(() =>
                VerifyFailedFetchAsync(reference, timeout.Token)).ConfigureAwait(true);
    }

    private static async Task VerifyCancelledFetchAsync(IOperationPort port, CancellationToken token)
    {
        var started = await port.StartFetchAsync(FetchMode.Stalled, token).ConfigureAwait(false);
        Assert.NotEqual(Guid.Empty, started.Id);
        Assert.Equal(started.Id, started.EnvelopeId);
        Assert.Equal("git_fetch", started.Tool);
        var running = await port.StatusAsync(started.Id, Caller.OtherTransport, token).ConfigureAwait(false);
        Assert.Equal(new OperationStatus("ok", started.Id, "running", true), running);
        Assert.Equal("operation_pending", (await port.ResultAsync(started.Id, Caller.Owner, token).ConfigureAwait(false)).Code);
        Assert.Equal("operation_unavailable", (await port.StatusAsync(started.Id, Caller.ForeignGrant, token).ConfigureAwait(false)).Code);
        Assert.Equal("operation_unavailable", await port.CancelAsync(started.Id, Caller.ForeignGrant, token).ConfigureAwait(false));
        Assert.Equal("ok", await port.CancelAsync(started.Id, Caller.Owner, token).ConfigureAwait(false));

        var finished = await WaitForTerminalAsync(port, started.Id, token).ConfigureAwait(false);
        Assert.Equal(new OperationStatus("ok", started.Id, "cancelled", false), finished);
        var result = await port.ResultAsync(started.Id, Caller.OtherTransport, token).ConfigureAwait(false);
        Assert.Equal(new OperationResult("ok", started.Id, "cancelled"), result);
        Assert.True(await port.WasStoppedAsync(token).ConfigureAwait(false), "The owning cancellation should stop the native operation.");
    }

    private static async Task VerifyCompletedFetchAsync(IOperationPort port, CancellationToken token)
    {
        var started = await port.StartFetchAsync(FetchMode.Succeeds, token).ConfigureAwait(false);
        Assert.NotEqual(Guid.Empty, started.Id);
        Assert.Equal(started.Id, started.EnvelopeId);
        var finished = await WaitForTerminalAsync(port, started.Id, token).ConfigureAwait(false);
        Assert.Equal(new OperationStatus("ok", started.Id, "succeeded", false), finished);
        Assert.Equal(new OperationResult("ok", started.Id, "ok"),
            await port.ResultAsync(started.Id, Caller.OtherTransport, token).ConfigureAwait(false));
    }

    private static async Task VerifyFailedFetchAsync(IOperationPort port, CancellationToken token)
    {
        var started = await port.StartFetchAsync(FetchMode.Fails, token).ConfigureAwait(false);
        Assert.NotEqual(Guid.Empty, started.Id);
        Assert.Equal(started.Id, started.EnvelopeId);
        Assert.Equal("git_fetch", started.Tool);
        var finished = await WaitForTerminalAsync(port, started.Id, token).ConfigureAwait(false);
        Assert.Equal(new OperationStatus("ok", started.Id, "failed", false), finished);
        var expected = new OperationResult("ok", started.Id, "git_fetch_failed");
        Assert.Equal(expected, await port.ResultAsync(started.Id, Caller.OtherTransport, token).ConfigureAwait(false));
        Assert.Equal(new OperationResult("operation_unavailable", Guid.Empty, null),
            await port.ResultAsync(started.Id, Caller.ForeignGrant, token).ConfigureAwait(false));
        Assert.Equal(expected, await port.ResultAsync(started.Id, Caller.Owner, token).ConfigureAwait(false));
    }

    private static async Task<OperationStatus> WaitForTerminalAsync(IOperationPort port, Guid id, CancellationToken token)
    {
        while (true)
        {
            var state = await port.StatusAsync(id, Caller.OtherTransport, token).ConfigureAwait(false);
            Assert.Equal("ok", state.Code);
            if (state.State is "succeeded" or "failed" or "cancelled")
                return state;
            await Task.Delay(20, token).ConfigureAwait(false);
        }
    }

    private enum Caller { Owner, OtherTransport, ForeignGrant }
    private enum FetchMode { Stalled, Succeeds, Fails }
    private sealed record StartedOperation(Guid Id, Guid EnvelopeId, string? Tool);
    private sealed record OperationStatus(string? Code, Guid Id, string? State, bool CanCancel);
    private sealed record OperationResult(string? Code, Guid Id, string? Outcome);

    private interface IOperationPort
    {
        Task<StartedOperation> StartFetchAsync(FetchMode mode, CancellationToken token);
        Task<OperationStatus> StatusAsync(Guid id, Caller caller, CancellationToken token);
        Task<OperationResult> ResultAsync(Guid id, Caller caller, CancellationToken token);
        Task<string?> CancelAsync(Guid id, Caller caller, CancellationToken token);
        Task<bool> WasStoppedAsync(CancellationToken token);
    }

    private sealed class ReferencePort(string? fault) : IOperationPort
    {
        private Guid _operation;
        private string _state = "succeeded";

        public Task<StartedOperation> StartFetchAsync(FetchMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _operation = Guid.NewGuid();
            _state = mode switch
            {
                FetchMode.Stalled => "running",
                FetchMode.Fails when !string.Equals(fault, "wrong-failure-state", StringComparison.Ordinal) => "failed",
                _ => "succeeded",
            };
            return Task.FromResult(new StartedOperation(_operation,
                string.Equals(fault, "wrong-handle", StringComparison.Ordinal) ? Guid.Empty : _operation, "git_fetch"));
        }

        public Task<OperationStatus> StatusAsync(Guid id, Caller caller, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(id != _operation || Foreign(caller, "foreign-status")
                ? new OperationStatus("operation_unavailable", Guid.Empty, null, false)
                : new OperationStatus("ok", id, _state, string.Equals(_state, "running", StringComparison.Ordinal)));
        }

        public Task<OperationResult> ResultAsync(Guid id, Caller caller, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (id != _operation || Foreign(caller, "foreign-result"))
                return Task.FromResult(new OperationResult("operation_unavailable", Guid.Empty, null));
            if (string.Equals(_state, "running", StringComparison.Ordinal) &&
                !string.Equals(fault, "premature-result", StringComparison.Ordinal))
            {
                return Task.FromResult(new OperationResult("operation_pending", Guid.Empty, null));
            }

            var outcome = _state switch
            {
                "cancelled" when !string.Equals(fault, "wrong-result", StringComparison.Ordinal) => "cancelled",
                "failed" when !string.Equals(fault, "wrong-native-failure", StringComparison.Ordinal) => "git_fetch_failed",
                _ => "ok",
            };
            return Task.FromResult(new OperationResult("ok", id, outcome));
        }

        public Task<string?> CancelAsync(Guid id, Caller caller, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (id != _operation || Foreign(caller, "foreign-cancel"))
                return Task.FromResult<string?>("operation_unavailable");

            _state = string.Equals(fault, "wrong-final-state", StringComparison.Ordinal) ? "failed" : "cancelled";
            return Task.FromResult<string?>("ok");
        }

        public Task<bool> WasStoppedAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(string.Equals(_state, "cancelled", StringComparison.Ordinal));
        }

        private bool Foreign(Caller caller, string control) =>
            caller == Caller.ForeignGrant && !string.Equals(fault, control, StringComparison.Ordinal);
    }
}
