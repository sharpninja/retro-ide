using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RetroIde.Application.Emulators;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises emulator bundle metadata and archive verification.</summary>
public sealed partial class EmulatorBundleIntegrationTests
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

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Matching_archives_stage_regular_assets_and_preserve_executable_permission()
    {
        var staging = Directory.CreateTempSubdirectory("retroide-bundle-");
        using var archive = Archive(("adapter/Uae.dll", 0), ("bin/uae", 0x81ed), ("profiles/amiga-aga.json", 0), ("bin/runtime.so", 0), ("bin/", 0x41ed));
        var manifest = Parse(Manifest);
        var artifact = manifest.TargetFor("linux-x64", new Version(1, 0)) with { Sha256 = Digest(archive) };
        try
        {
            var installation = await EmulatorBundleArchive.StageAsync(staging.FullName, archive, artifact,
                manifest.DebuggerProfiles.Values, TestContext.Current.CancellationToken);
            Assert.Single(Directory.GetDirectories(staging.FullName));
            Assert.Empty(Directory.GetFiles(installation));
            var executable = Path.Combine(installation, "files/bin/uae");
            Assert.Equal("bin/uae", await File.ReadAllTextAsync(executable, TestContext.Current.CancellationToken), StringComparer.Ordinal);
            Assert.True(File.Exists(Path.Combine(installation, "files/adapter/Uae.dll")));
            Assert.True(File.Exists(Path.Combine(installation, "files/profiles/amiga-aga.json")));
            Assert.True(File.Exists(Path.Combine(installation, "files/bin/runtime.so")));
            if (!OperatingSystem.IsWindows())
                Assert.True(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute));
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Theory]
    [InlineData("sha256", "bin/uae", 0)]
    [InlineData("entry point", "bin/other", 0)]
    [InlineData("entry point", "bin/uae/", 0)]
    [InlineData("archive path", "../outside", 0)]
    [InlineData("archive path", "bin\\uae", 0)]
    [InlineData("archive path", "C:/bin/uae", 0)]
    [InlineData("alias", "Adapter/readme", 0)]
    [InlineData("alias", "adapter/Uae.dll", 0)]
    [InlineData("alias", "adapter/Uae.dll/child", 0)]
    [InlineData("regular file", "bin/uae", 0xa1ff)]
    public async Task Rejected_archives_leave_no_staged_installation(string errorText, string entry, int mode)
    {
        var staging = Directory.CreateTempSubdirectory("retroide-bundle-");
        using var archive = Archive(("adapter/Uae.dll", 0), (entry, mode), ("profiles/amiga-aga.json", 0));
        var manifest = Parse(Manifest);
        var artifact = manifest.TargetFor("linux-x64", new Version(1, 0));
        if (!string.Equals(errorText, "sha256", StringComparison.Ordinal))
            artifact = artifact with { Sha256 = Digest(archive) };
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                EmulatorBundleArchive.StageAsync(staging.FullName, archive, artifact,
                    manifest.DebuggerProfiles.Values, TestContext.Current.CancellationToken));
            Assert.Contains(errorText, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    /// <remarks>TR-RETRO-BUNDLE-009#ac-bundle-transaction; TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Incomplete_archives_leave_no_staged_installation()
    {
        var staging = Directory.CreateTempSubdirectory("retroide-bundle-");
        using var archive = Archive(("adapter/Uae.dll", 0), ("bin/uae", 0));
        var manifest = Parse(Manifest);
        var artifact = manifest.TargetFor("linux-x64", new Version(1, 0)) with { Sha256 = Digest(archive) };
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                EmulatorBundleArchive.StageAsync(staging.FullName, archive, artifact,
                    manifest.DebuggerProfiles.Values, TestContext.Current.CancellationToken));
            Assert.Contains("debugger profile", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    /// <remarks>TEST-RETRO-009#ac-test-bundle-integrity.</remarks>
    [Fact]
    public async Task Cancellation_during_the_archive_copy_removes_partial_files()
    {
        var staging = Directory.CreateTempSubdirectory("retroide-bundle-");
        using var zip = Archive(("adapter/Uae.dll", 0), ("bin/uae", 0));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var archive = new InterruptedArchive(zip.ToArray(), cancellation);
        var artifact = Parse(Manifest).TargetFor("linux-x64", new Version(1, 0)) with { Sha256 = Digest(zip) };
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                EmulatorBundleArchive.StageAsync(staging.FullName, archive, artifact, [], cancellation.Token));
            Assert.Equal(12, archive.Position);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    private sealed class InterruptedArchive(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position != 0)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return await base.ReadAsync(buffer[..12], cancellationToken).ConfigureAwait(false);
        }
    }

    private static MemoryStream Archive(params (string Path, int Mode)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, mode) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                entry.ExternalAttributes = mode << 16;
                if (!path.EndsWith('/'))
                {
                    using var file = entry.Open();
                    file.Write(Encoding.UTF8.GetBytes(path));
                }
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string Digest(MemoryStream stream) => Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));

    private static EmulatorBundleManifest Parse(string text) => EmulatorBundleManifest.Parse(Encoding.UTF8.GetBytes(text));
}
