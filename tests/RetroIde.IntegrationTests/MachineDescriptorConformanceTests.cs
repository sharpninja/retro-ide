using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies exact target description, device addresses, and live map refresh.</summary>
public sealed class MachineDescriptorConformanceTests
{
    /// <summary>Checks a multi-CPU target's identities, reference timing, byte order, and addressable ranges.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
    [Fact]
    public async Task A_machine_description_preserves_exact_model_and_address_space_details()
    {
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        var result = await control.DescribeMachineAsync(adapter.NewScope(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded, result.Error?.Message);
        var machine = result.Value!;
        Assert.Equal(adapter.Identity, machine.Session);
        Assert.Equal(7UL, machine.MapRevision);
        Assert.Equal(ByteOrder.BigEndian, machine.DefaultByteOrder);
        Assert.Equal(new DebuggerProfileReference("amiga-aga", 1), machine.Profile);
        Assert.Collection(machine.Cpus,
            cpu => Assert.Equal(["cpu", "68000", "68040", "mc68040"], new[] { cpu.Id, cpu.Family, cpu.Model, cpu.Variant },
                StringComparer.Ordinal),
            cpu =>
            {
                Assert.Equal("z84c00", cpu.Variant, StringComparer.Ordinal);
                Assert.Equal(ByteOrder.LittleEndian, cpu.DefaultByteOrder);
                Assert.Equal(["aux"], cpu.MemorySpaceIds);
            });
        Assert.Equal(25_000_000UL, machine.Clocks[0].NominalHertz);
        Assert.Equal(["bus-rise", "bus-fall"], machine.Clocks[0].Phases);
        Assert.Equal(16, machine.MemorySpaces[0].AddressBits);
        Assert.Equal(2, machine.MemorySpaces[1].BytesPerAddress);
        Assert.Equal(ByteOrder.LittleEndian, machine.MemorySpaces[1].DefaultByteOrder);
        Assert.Equal(0xC0FFUL, machine.MemorySpaces[0].Regions.Single(region => region.Kind == MemoryRegionKind.Device).End);
        Assert.Equal("lower", machine.MemorySpaces[0].ActiveMappings["window"], StringComparer.Ordinal);
    }

    /// <summary>Checks publication on an ordered map event, then replay recovery after a cursor gap.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
    [Fact]
    public async Task A_map_watch_refreshes_after_a_bank_change_then_recovers_after_an_event_gap()
    {
        var adapter = new MachineDebugAdapterFixture();
        var control = adapter.OpenControl();
        adapter.Events[1] = [new MachineMapChanged(new(adapter.Identity, 2), 2, 8),
            new SessionEventGap(new(adapter.Identity, 4), 3)];
        adapter.OnEvent = item =>
        {
            if (item is MachineMapChanged)
            {
                adapter.Machine = MachineDebugAdapterFixture.DescribeTarget(adapter.Identity, 8, upperBank: true);
                adapter.State = adapter.MakeState(2, 2);
            }
            else
            {
                adapter.Machine = MachineDebugAdapterFixture.DescribeTarget(adapter.Identity, 9, upperBank: false, bootOverlay: false);
                adapter.State = adapter.MakeState(5, 4);
            }
        };
        var token = TestContext.Current.CancellationToken;

        var descriptors = await control.WatchMachineAsync(adapter.NewScope(), null, token).ToArrayAsync(token).ConfigureAwait(true);

        Assert.All(descriptors, item => Assert.True(item.Succeeded, item.Error?.Message));
        Assert.Equal([7UL, 8UL, 9UL], descriptors.Select(item => item.Value!.MapRevision));
        Assert.Equal("upper", descriptors[1].Value!.MemorySpaces[0].ActiveMappings["window"], StringComparer.Ordinal);
        Assert.False(descriptors[2].Value!.MemorySpaces[0].ActiveMappings.ContainsKey("boot"));
        Assert.Equal([1UL, 5UL], adapter.Subscriptions);
        adapter.Machine = descriptors[1].Value!;
        var stale = await control.DescribeMachineAsync(adapter.NewScope(), token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.Incompatible, stale.Error?.Code, StringComparer.Ordinal);
    }
}
