using System.Collections.Immutable;
using RetroIde.Emulation;
using System.Collections.Immutable;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the live region, mapping, permission and address-width checks used by inspection.</summary>
public sealed class MachineMemoryMapTests
{
    /// <remarks>TEST-RETRO-010#ac-test-control-semantics; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_mapped_operation_revalidates_state_after_its_description_reply(bool write, bool withdraw)
    {
        var token = TestContext.Current.CancellationToken;
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        var scope = adapter.NewScope() with { ExpectedRevision = adapter.State.StateRevision };
        var address = new MemoryAddress("main", 0, "cpu", MapRevision: 7);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<AdapterResult<MachineDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.DescribeOverride = cancellation =>
        {
            started.TrySetResult();
            return new(reply.Task.WaitAsync(cancellation));
        };
        var result = ReadOrWriteAsync();
        try
        {
            await started.Task.WaitAsync(token);
            var operation = write ? IMemoryPort.WriteMapped : IMemoryPort.InspectMapped;
            adapter.State = withdraw
                ? adapter.State with
                {
                    Cursor = new(adapter.Identity, 2), Capabilities = new(2, adapter.State.Capabilities.Items.Select(item =>
                        item.Id == IMemoryPort.Capability && string.Equals(item.SubjectId, "main", StringComparison.Ordinal)
                            ? item with { Operations = item.Operations.Remove(operation) } : item).ToImmutableArray()),
                }
                : adapter.MakeState(2, 2);
            adapter.Events[1] = [new SessionStateChanged(adapter.State)];
            Assert.True(Assert.Single(await control.WatchAsync(new(adapter.Identity, 1), token).ToArrayAsync(token)).Succeeded);
        }
        finally { reply.TrySetResult(AdapterResult<MachineDescriptor>.Success(adapter.Machine)); }

        var error = await result;
        Assert.Empty(adapter.Reads);
        Assert.Equal(0, adapter.WriteAttempts);
        Assert.Equal(withdraw ? AdapterErrorCodes.Unsupported : AdapterErrorCodes.RevisionConflict, error?.Code, StringComparer.Ordinal);
        Assert.Equal(scope.OperationId, error?.OperationId);

        async Task<AdapterError?> ReadOrWriteAsync() => write
            ? (await control.WriteMemoryAsync(new(scope, new(address, [1])), token)).Error
            : (await control.ReadMemoryAsync(new(scope, address, 1), token)).Error;
    }

    /// <summary>Proves that a mapped read cannot cross an overlay, hit a device, or reuse another CPU's map.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Fact]
    public async Task Mapped_reads_enforce_active_regions_permissions_units_and_cpu_scope()
    {
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;

        await CheckAsync(new("main", 0x8000, "cpu", "upper", 7), 1, AdapterErrorCodes.InvalidArgument);
        await CheckAsync(new("main", 0x8FFF, "cpu", "lower", 7), 2, AdapterErrorCodes.InvalidArgument);
        await CheckAsync(new("main", 0x00FF, "cpu", MapRevision: 7), 2, AdapterErrorCodes.InvalidArgument);
        await CheckAsync(new("main", 0xC000, "cpu", MapRevision: 7), 1, AdapterErrorCodes.PermissionDenied);
        await CheckAsync(new("main", 0x8000, "io-cpu", "lower", 7), 1, AdapterErrorCodes.InvalidArgument);
        await CheckAsync(new("main", ulong.MaxValue, "cpu", MapRevision: 7), 2, AdapterErrorCodes.InvalidArgument);
        await CheckAsync(new("aux", 0xFFFF, "io-cpu", MapRevision: 7), 3, AdapterErrorCodes.InvalidArgument);
        Assert.Empty(adapter.Reads);

        await CheckAsync(new("main", 0x8000, "cpu", "lower", 7), 1, null);
        await CheckAsync(new("main", 0x0100, "cpu", "boot-rom", 7), 2, null);
        await CheckAsync(new("main", 0xE000, "cpu", "ram-alias", 7), 1, null);
        await CheckAsync(new("main", 0xF0FF, "cpu", "far-segment", 7), 1, null);
        await CheckAsync(new("aux", 0xFFFF, "io-cpu", MapRevision: 7), 2, null);
        Assert.Equal(5, adapter.Reads.Count);

        async Task CheckAsync(MemoryAddress address, int bytes, string? failure)
        {
            var operation = new MemoryReadRequest(adapter.NewScope(), address, bytes);
            var result = await control.ReadMemoryAsync(operation, token).ConfigureAwait(true);
            Assert.Equal(failure, result.Error?.Code, StringComparer.Ordinal);
            if (failure is null)
            {
                Assert.Equal(address, result.Value!.Address);
                Assert.Equal(bytes, result.Value.Data.Length);
            }
        }
    }

    /// <summary>Checks pre-dispatch validation and the host's final check if a bank changes before the read.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Fact]
    public async Task The_host_checks_map_revision_again_after_workbench_validation()
    {
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;
        var request = new MemoryReadRequest(adapter.NewScope(), new("main", 0x8000, "cpu", "lower", 7), 1);
        adapter.BeforeRead = () =>
            adapter.Machine = MachineDebugAdapterFixture.DescribeTarget(adapter.Identity, 8, upperBank: true);

        var changedMidRead = await control.ReadMemoryAsync(request, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.RevisionConflict, changedMidRead.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(8UL, changedMidRead.Error?.CurrentMapRevision);
        Assert.Single(adapter.Reads);
        var rejectedBeforeDispatch = await control.ReadMemoryAsync(request, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.RevisionConflict, rejectedBeforeDispatch.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(8UL, rejectedBeforeDispatch.Error?.CurrentMapRevision);
        Assert.Single(adapter.Reads);

        var exact = request with { Scope = adapter.NewScope(), Address = new("main", 0x8000, "cpu", "upper", 8) };
        Assert.True((await control.ReadMemoryAsync(exact, token).ConfigureAwait(true)).Succeeded);
        Assert.Equal(2, adapter.Reads.Count);
    }

    /// <summary>Checks inclusive limits and overflow at the highest valid 64-bit address.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Fact]
    public async Task Reads_can_end_at_the_final_64_bit_address_and_cannot_wrap_around()
    {
        var adapter = new MachineDebugAdapterFixture();
        var memory = adapter.Machine.MemorySpaces[0] with
        {
            AddressBits = 64,
            Regions = [new("top-ram", ulong.MaxValue - 1, ulong.MaxValue, MemoryRegionKind.Ram, MemoryAccess.Inspect)],
            ActiveMappings = ImmutableDictionary<string, string>.Empty,
        };
        adapter.Machine = adapter.Machine with { MemorySpaces = adapter.Machine.MemorySpaces.SetItem(0, memory) };
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;
        var request = new MemoryReadRequest(adapter.NewScope(), new("main", ulong.MaxValue - 1, "cpu", MapRevision: 7), 2);

        var valid = await control.ReadMemoryAsync(request, token).ConfigureAwait(true);
        Assert.Equal(2, valid.Value?.Data.Length);
        var wrapped = await control.ReadMemoryAsync(request with { Scope = adapter.NewScope(), ByteCount = 3 }, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, wrapped.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(request, Assert.Single(adapter.Reads));
    }

    /// <summary>Checks that negotiated writes use the active map permissions and both revisions.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance; TEST-RETRO-017#ac-test-machine-descriptor-contract; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Fact]
    public async Task A_memory_write_cannot_reach_rom_raw_space_or_exceed_the_negotiated_limit()
    {
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;
        var scope = adapter.NewScope() with { ExpectedRevision = adapter.State.StateRevision };
        var request = new MutationRequest<MemoryWriteRequest>(scope, new(new("main", 0x0100, "cpu", "boot-rom", 7), [1]));

        var rom = await control.WriteMemoryAsync(request, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.PermissionDenied, rom.Error?.Code, StringComparer.Ordinal);
        var raw = await control.WriteMemoryAsync(request with { Payload = new(new("main", 0), [1]) }, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, raw.Error?.Code, StringComparer.Ordinal);
        var ram = request with { Payload = new(new("main", 0, "cpu", MapRevision: 7), [1, 2]) };
        var oversize = await control.WriteMemoryAsync(ram with { Payload = ram.Payload with { Data = [1, 2, 3] } }, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, oversize.Error?.Code, StringComparer.Ordinal);
        Assert.Empty(adapter.Writes);
        Assert.Equal(new MutationReceipt(scope, 2), (await control.WriteMemoryAsync(ram, token).ConfigureAwait(true)).Value);
        Assert.Equal(ram, Assert.Single(adapter.Writes));
    }

    /// <remarks>TEST-RETRO-010#ac-test-control-semantics; TEST-RETRO-017#ac-test-live-machine-maps.</remarks>
    [Fact]
    public async Task A_reconciled_mapped_write_keeps_its_exact_address_and_bytes()
    {
        var adapter = new MachineDebugAdapterFixture { DropNextWriteReply = true };
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;
        var scope = adapter.NewScope() with { ExpectedRevision = adapter.State.StateRevision };
        var request = new MutationRequest<MemoryWriteRequest>(scope, new(new("main", 0x00, "cpu", MapRevision: 7), [1, 2]));
        Assert.Equal(AdapterErrorCodes.UnknownOutcome, (await control.WriteMemoryAsync(request, token)).Error?.Code, StringComparer.Ordinal);
        Assert.Equal(MutationDisposition.Applied, (await control.ReconcileAsync(scope, token)).Value?.Disposition);

        var altered = request with { Payload = request.Payload with { Data = [2, 1] } };
        Assert.Equal(AdapterErrorCodes.InvalidArgument, (await control.WriteMemoryAsync(altered, token)).Error?.Code, StringComparer.Ordinal);
        var moved = request with { Payload = request.Payload with { Address = request.Payload.Address with { Offset = 2 } } };
        Assert.Equal(AdapterErrorCodes.InvalidArgument, (await control.WriteMemoryAsync(moved, token)).Error?.Code, StringComparer.Ordinal);
        var sameBytes = request with { Payload = request.Payload with { Data = [1, 2] } };
        Assert.Equal(new MutationReceipt(scope, 2), (await control.WriteMemoryAsync(sameBytes, token)).Value);
        Assert.Equal(request, Assert.Single(adapter.Writes));
    }
}
