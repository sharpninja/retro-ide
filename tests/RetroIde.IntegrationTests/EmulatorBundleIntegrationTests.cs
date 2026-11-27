using System.Text;
using RetroIde.Application.Emulators;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed class EmulatorBundleIntegrationTests
{
    private const string WindowsHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LinuxHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Manifest = $$"""
        {
          "schemaVersion": 1,
          "id": "uae",
          "version": "1.2.0-beta.1",
          "name": "UAE",
          "description": "UAE emulator and Retro IDE binding.",
          "homepage": "https://example.invalid/uae",
          "license": "GPL-2.0-or-later",
          "adapterId": "uae",
          "adapterApi": ["1.0", "1.1"],
          "targets": {
            "win-x64": {
              "url": "https://example.invalid/uae-win-x64.zip",
              "sha256": "{{WindowsHash}}",
              "entryPoints": {"binding": "adapter/Uae.dll", "emulator": "bin/uae.exe"}
            },
            "linux-x64": {
              "url": "https://example.invalid/uae-linux-x64.zip",
              "sha256": "{{LinuxHash}}",
              "entryPoints": {"binding": "adapter/Uae.dll", "emulator": "bin/uae"}
            }
          },
          "debuggerProfiles": {
            "amiga-aga": {"schemaVersion": 1, "path": "profiles/amiga-aga.json"}
          }
        }
        """;

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest; TEST-RETRO-009#ac-test-bucket-platforms.</remarks>
    [Fact]
    public void A_manifest_selects_distinct_artifacts_for_supported_hosts_and_the_adapter_contract()
    {
        var bundle = Parse(Manifest);
        Assert.Equal("uae", bundle.Id, StringComparer.Ordinal);
        Assert.Equal("1.2.0-beta.1", bundle.Version, StringComparer.Ordinal);
        Assert.Equal("uae", bundle.AdapterId, StringComparer.Ordinal);
        Assert.Equal("GPL-2.0-or-later", bundle.License, StringComparer.Ordinal);
        var windows = bundle.TargetFor("win-x64", new Version(1, 1));
        Assert.Equal(WindowsHash, windows.Sha256, StringComparer.Ordinal);
        Assert.Equal("bin/uae.exe", windows.EntryPoints["emulator"], StringComparer.Ordinal);
        var linux = bundle.TargetFor("linux-x64", new Version(1, 0));
        Assert.Equal(LinuxHash, linux.Sha256, StringComparer.Ordinal);
        Assert.Equal("adapter/Uae.dll", linux.EntryPoints["binding"], StringComparer.Ordinal);
        Assert.Equal("bin/uae", linux.EntryPoints["emulator"], StringComparer.Ordinal);
        var profile = bundle.DebuggerProfiles["amiga-aga"];
        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal("profiles/amiga-aga.json", profile.Path, StringComparer.Ordinal);
        Assert.Contains("host target", Assert.Throws<InvalidOperationException>(() =>
            bundle.TargetFor("linux-arm64", new Version(1, 1))).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adapter API", Assert.Throws<InvalidOperationException>(() =>
            bundle.TargetFor("linux-x64", new Version(2, 0))).Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bucket-manifest; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2", "schemaVersion")]
    [InlineData("\"id\": \"uae\"", "\"id\": \"uae\", \"id\": \"other\"", "id")]
    [InlineData("\"version\": \"1.2.0-beta.1\"", "\"version\": \"../latest\"", "version")]
    [InlineData("\"1.0\", \"1.1\"", "\"latest\"", "adapterApi")]
    [InlineData(WindowsHash, "bad", "sha256")]
    [InlineData("\"binding\": \"adapter/Uae.dll\"", "\"extra\": \"adapter/Uae.dll\"", "binding")]
    [InlineData("adapter/Uae.dll", "../Uae.dll", "entryPoints")]
    [InlineData("adapter/Uae.dll", "C:/adapter/Uae.dll", "entryPoints")]
    [InlineData("adapter/Uae.dll", "adapter\\\\Uae.dll", "entryPoints")]
    [InlineData("adapter/Uae.dll", "adapter/con.dll", "entryPoints")]
    [InlineData("profiles/amiga-aga.json", "profiles/../../base.json", "debuggerProfiles")]
    public void Invalid_metadata_is_rejected_before_installation(string original, string changed, string field)
    {
        Assert.Contains(original, Manifest, StringComparison.Ordinal);
        var invalid = Manifest.Replace(original, changed, StringComparison.Ordinal);
        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => Parse(invalid)).Message, StringComparison.Ordinal);
    }

    private static EmulatorBundleManifest Parse(string text) => EmulatorBundleManifest.Parse(Encoding.UTF8.GetBytes(text));
}
