using System.Text;
using System.Text.Json.Nodes;
using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <summary>Checks hash-verified profile installation, rejection of changes, and compatible legacy receipts.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-compatibility.</remarks>
    [Fact]
    public async Task A_pinned_profile_is_available_only_when_its_content_and_receipt_are_verified()
    {
        using var root = new BundleStoreFixture();
        using var archive = LinuxReleaseArchive();
        var token = root.CancellationToken;
        var installed = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, ReleaseManifest(FirstPin, Digest(archive)), archive, token)
            .ConfigureAwait(true);
        var reference = new DebuggerProfileReference("amiga-aga", 1);
        var verified = installed.ReadDebuggerProfile(reference);
        Assert.True(verified.Succeeded, verified.Error?.Message);
        Assert.Equal(2, verified.Value!.BytesPerGroup);
        Assert.Equal(AdapterErrorCodes.Unavailable, installed.ReadDebuggerProfile(new("absent", 1)).Error?.Code, StringComparer.Ordinal);

        var profile = Path.Combine(installed.Directory, "files", DebuggerFixturePath);
        File.AppendAllText(profile, " ");
        Assert.Equal(AdapterErrorCodes.Incompatible, installed.ReadDebuggerProfile(reference).Error?.Code, StringComparer.Ordinal);
        File.Delete(profile);
        Assert.NotNull(root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));

        var receipt = Path.Combine(installed.Directory, "receipt.json");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(receipt, token).ConfigureAwait(true))!.AsObject();
        Assert.True(legacy.Remove("debuggerProfilesSha256"));
        await File.WriteAllTextAsync(receipt, legacy.ToJsonString(), token).ConfigureAwait(true);
        var reopened = root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi);
        Assert.NotNull(reopened);
        Assert.True(File.Exists(reopened.EntryPoint("binding")));
        Assert.Equal(AdapterErrorCodes.Unavailable, reopened.ReadDebuggerProfile(reference).Error?.Code, StringComparer.Ordinal);
    }

    /// <summary>Checks schema validation in staging and non-disruptive handling of a newer optional format.</summary>
    /// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-compatibility.</remarks>
    [Theory]
    [InlineData("""{"schemaVersion":1,"id":"wrong-id"}""", 1, false)]
    [InlineData("""{"schemaVersion":1,"id":"amiga-aga","id":"amiga-aga"}""", 1, false)]
    [InlineData("""{"schemaVersion":1,"id":"amiga-aga"}""", 1, false)]
    [InlineData("""{"schemaVersion":2,"id":"amiga-aga","newForm":true}""", 2, true)]
    public async Task The_installer_checks_profile_envelopes_and_known_schemas_before_publication(string profile, int schema, bool compatiblePackage)
    {
        using var root = new BundleStoreFixture();
        using var archive = ArchiveWithDebuggerProfile(profile, ("adapter/Uae.dll", 0), ("bin/uae", 0x81ed), (DebuggerFixturePath, 0));
        var manifest = ReleaseManifest(FirstPin, Digest(archive));
        if (schema == 2)
        {
            var supported = JsonNode.Parse(manifest)!;
            supported["debuggerProfiles"]!["amiga-aga"]!["schemaVersion"] = schema;
            manifest = Encoding.UTF8.GetBytes(supported.ToJsonString());
        }

        if (compatiblePackage)
        {
            var bundle = await root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, manifest, archive, root.CancellationToken).ConfigureAwait(true);
            Assert.True(File.Exists(bundle.EntryPoint("binding")));
            Assert.Equal(AdapterErrorCodes.Unsupported, bundle.ReadDebuggerProfile(new("amiga-aga", schema)).Error?.Code, StringComparer.Ordinal);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                root.Store.InstallAsync(FirstPin, RuntimeIdentifier, AdapterApi, manifest, archive, root.CancellationToken)).ConfigureAwait(true);
            Assert.Null(root.Store.Resolve(FirstPin, RuntimeIdentifier, AdapterApi));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Directory, "staging")));
        }
    }
}
