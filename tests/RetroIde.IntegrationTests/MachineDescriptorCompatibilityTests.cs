using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies that optional metadata cannot disable a usable raw inspection capability.</summary>
public sealed class MachineDescriptorCompatibilityTests
{
    /// <summary>Checks unavailable, incompatible, malformed and foreign descriptors against the independent raw path.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-compatibility.</remarks>
    [Theory]
    [InlineData("no-port", AdapterErrorCodes.Unsupported)]
    [InlineData("different-version", AdapterErrorCodes.Incompatible)]
    [InlineData("invalid-range", AdapterErrorCodes.Incompatible)]
    [InlineData("ambiguous-map", AdapterErrorCodes.Incompatible)]
    [InlineData("different-session", AdapterErrorCodes.Incompatible)]
    [InlineData("missing-clock", AdapterErrorCodes.Incompatible)]
    public async Task Unusable_description_preserves_only_advertised_bounded_raw_inspection(string condition, string expectedCode)
    {
        var adapter = new MachineDebugAdapterFixture(advertised: !string.Equals(condition, "no-port", StringComparison.Ordinal));
        adapter.Machine = Reconfigure(adapter.Machine, condition);
        var control = adapter.OpenControl();
        var token = TestContext.Current.CancellationToken;
        var unavailable = await control.DescribeMachineAsync(adapter.NewScope(), token).ConfigureAwait(true);
        Assert.Equal(expectedCode, unavailable.Error?.Code, StringComparer.Ordinal);

        var raw = new MemoryReadRequest(adapter.NewScope(), new("main", 0xFFFF), 1);
        Assert.Equal(new byte[] { 0xA5 }, (await control.ReadMemoryAsync(raw, token).ConfigureAwait(true)).Value!.Data.ToArray());
        var invalid = await control.ReadMemoryAsync(raw with { ByteCount = 2 }, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.InvalidArgument, invalid.Error?.Code, StringComparer.Ordinal);
        var mapped = raw with { Address = new("main", 0x8000, "cpu", "lower", 7) };
        Assert.Equal(expectedCode, (await control.ReadMemoryAsync(mapped, token).ConfigureAwait(true)).Error?.Code, StringComparer.Ordinal);
        var absent = await control.ReadMemoryAsync(raw with { Address = new("aux", 0) }, token).ConfigureAwait(true);
        Assert.Equal(AdapterErrorCodes.Unsupported, absent.Error?.Code, StringComparer.Ordinal);
        Assert.Equal(raw, Assert.Single(adapter.Reads));
        Assert.True((await control.ReadAsync(token).ConfigureAwait(true)).Succeeded);
    }

    private static MachineDescriptor Reconfigure(MachineDescriptor machine, string condition)
    {
        var space = machine.MemorySpaces[0];
        return condition switch
        {
            "different-version" => machine with { Version = new(2, 0) },
            "invalid-range" => machine with { MemorySpaces = machine.MemorySpaces.SetItem(0,
                space with { Regions = space.Regions.SetItem(0, space.Regions[0] with { End = 0x1_0000 }) }) },
            "ambiguous-map" => machine with { MemorySpaces = machine.MemorySpaces.SetItem(0,
                space with { Regions = space.Regions.Add(new("ambiguous", 4, 5, MemoryRegionKind.Ram, MemoryAccess.Inspect)) }) },
            "different-session" => machine with { Session = machine.Session with { InstanceId = Guid.NewGuid() } },
            "missing-clock" => machine with { Clocks = [] },
            _ => machine,
        };
    }
}
