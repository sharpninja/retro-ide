using System.Collections.Immutable;
using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the live region, mapping, permission and address-width checks used by inspection.</summary>
public sealed class MachineMemoryMapTests
{
    /// <summary>Proves that a mapped read cannot cross an overlay, hit a device, or reuse another CPU's map.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
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
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
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
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
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
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance; TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
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
}
