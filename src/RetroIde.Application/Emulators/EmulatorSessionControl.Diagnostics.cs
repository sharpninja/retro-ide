using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal ValueTask<AdapterResult<IConsolePort.Channels>> ListConsolesAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<IConsolePort, IConsolePort.Channels>(scope, IConsolePort.Capability, null, IConsolePort.List,
            (port, _) => port.ListAsync(scope, token), token);

    internal ValueTask<AdapterResult<IConsolePort.Records>> ReadConsoleAsync(ScopedQuery<IConsolePort.ChannelRead> request, CancellationToken token)
    {
        var page = request.Payload;
        return MachineMap.Identifier(page.ChannelId) && page.Limit is > 0 and <= 256
            ? InvokePortAsync<IConsolePort, IConsolePort.Records>(request.Scope, IConsolePort.Capability, page.ChannelId, IConsolePort.Read,
                async (port, descriptor) =>
                {
                    if (await CheckAdvertisedLimit(descriptor, request.Scope, "max-records", (ulong)page.Limit, "console page").ConfigureAwait(false) is { } invalid)
                        return AdapterResult<IConsolePort.Records>.Failure(invalid);
                    return await port.ReadAsync(request, token).ConfigureAwait(false);
                }, token)
            : InvalidPortInput<IConsolePort.Records>(request.Scope);
    }

    internal ValueTask<AdapterResult<MutationReceipt>> SendConsoleAsync(MutationRequest<IConsolePort.ChannelInput> request, CancellationToken token)
    {
        var input = request.Payload;
        return MachineMap.Identifier(input.ChannelId) && !input.Data.IsDefaultOrEmpty && input.Data.Length <= 4096
            ? MutatePortAsync<IConsolePort>(new(request.Scope, IConsolePort.Capability, input.ChannelId, IConsolePort.Send, input),
                (port, cancellation) => port.SendAsync(request, cancellation),
                (capability, _) => CheckAdvertisedLimit(capability, request.Scope, "max-input-bytes", (ulong)input.Data.Length, "console input"), token)
            : InvalidPortInput<MutationReceipt>(request.Scope);
    }

    internal ValueTask<AdapterResult<IConsolePort.Artifacts>> ListDiagnosticsAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<IConsolePort, IConsolePort.Artifacts>(scope, IConsolePort.Capability, null, IConsolePort.ListArtifacts,
            (port, _) => port.ListArtifactsAsync(scope, token), token);

    internal ValueTask<AdapterResult<ITestAutomationPort.Fixtures>> ListTestFixturesAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<ITestAutomationPort, ITestAutomationPort.Fixtures>(scope, ITestAutomationPort.Capability, null, ITestAutomationPort.List,
            (port, _) => port.ListAsync(scope, token), token);

    internal ValueTask<AdapterResult<MutationReceipt>> RunGuestFixtureAsync(MutationRequest<ITestAutomationPort.RunRequest> request,
        CancellationToken token)
    {
        var run = request.Payload;
        return MachineMap.Identifier(run.FixtureId) && run.Iterations is > 0 and <= 1000
            ? MutatePortAsync<ITestAutomationPort>(new(request.Scope, ITestAutomationPort.Capability, run.FixtureId, ITestAutomationPort.Run, run),
                (port, cancellation) => port.RunAsync(request, cancellation),
                (capability, _) => CheckAdvertisedLimit(capability, request.Scope, "max-iterations", (ulong)run.Iterations, "iteration"), token)
            : InvalidPortInput<MutationReceipt>(request.Scope);
    }

    internal ValueTask<AdapterResult<ITestAutomationPort.Report>> ReadTestReportAsync(ScopedQuery<ITestAutomationPort.RunIdentity> request,
        CancellationToken token) => request.Payload.OperationId != Guid.Empty
            ? InvokePortAsync<ITestAutomationPort, ITestAutomationPort.Report>(request.Scope, ITestAutomationPort.Capability, null,
                ITestAutomationPort.ReadResult, (port, _) => port.ReadAsync(request, token), token)
            : InvalidPortInput<ITestAutomationPort.Report>(request.Scope);

    internal ValueTask<AdapterResult<IGuestServicesPort.Snapshot>> ReadGuestServicesAsync(SessionOperation scope, CancellationToken token) =>
        InvokePortAsync<IGuestServicesPort, IGuestServicesPort.Snapshot>(scope, IGuestServicesPort.Capability, null, IGuestServicesPort.List,
            (port, _) => port.ReadAsync(scope, token), token);
}
