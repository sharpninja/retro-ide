using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the versioned contract shared by the emulator manager and bindings.</summary>
public sealed partial class AdapterConformanceTests
{
    private static readonly Version Initial = AdapterContract.CurrentVersion;
    private static readonly Version Later = new(1, 1);
    private static readonly Version Future = new(2, 0);

    /// <summary>Chooses the newest common version and allows explicit fallback to an older version.</summary>
    [Fact]
    public void Negotiation_checks_versions_from_the_client_receipt_and_binding()
    {
        var binding = new AdapterDescriptor("fixture", [Later, Future, Initial], [SessionMode.Launch, SessionMode.Attach], ["6502"]);
        var newest = AdapterContract.Negotiate("fixture", [Later, Initial], [Initial, Later, Future], binding, SessionMode.Launch);
        var fallback = AdapterContract.Negotiate("fixture", [Later, Initial], [Initial], binding, SessionMode.Attach);

        Assert.True(newest.Succeeded);
        Assert.Equal(Later, newest.Value);
        Assert.True(fallback.Succeeded);
        Assert.Equal(Initial, fallback.Value);
    }

    /// <summary>Rejects unregistered bindings, unsupported modes, and incompatible contract versions.</summary>
    [Theory]
    [InlineData("different-adapter", SessionMode.Launch, 0, AdapterErrorCodes.Incompatible)]
    [InlineData("fixture", SessionMode.Attach, 0, AdapterErrorCodes.Unsupported)]
    [InlineData("fixture", SessionMode.Launch, 1, AdapterErrorCodes.Incompatible)]
    public void Negotiation_rejects_an_incompatible_binding_before_open(
        string adapterId, SessionMode mode, int bundleMinor, string expectedCode)
    {
        var binding = new AdapterDescriptor("fixture", [Initial], [SessionMode.Launch], ["6502"]);

        var result = AdapterContract.Negotiate(adapterId, [new Version(1, bundleMinor)], [Initial], binding, mode);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(expectedCode, result.Error.Code, StringComparer.Ordinal);
    }
}
