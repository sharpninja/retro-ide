using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Application.Emulators;
using RetroIde.Testing;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>TEST-RETRO-009#ac-test-bundle-versions; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Package_tools_install_update_select_roll_back_and_protect_a_running_bundle()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = OpenEmulatorWorkbench(root);
        await using var http = await Connection.ConnectAsync(session, token);
        await using var stdio = await http.ConnectStdioAsync(Path.Combine(root.Directory, "packages-stdio.json"));
        await http.ChangeAsync("emulator_configure_bucket", BucketArguments(new Uri(git.Remote).AbsoluteUri));
        var project = Directory.CreateDirectory(Path.Combine(root.Directory, "project"));
        var rootId = (await http.ChangeAsync("workspace_open_root", new(StringComparer.Ordinal)
        {
            ["kind"] = "project", ["path"] = project.FullName.Replace('\\', '/'),
        })).GetProperty("data").GetProperty("id").GetGuid();
        var first = await PublishPackageAsync(root, git, FirstPin);
        await http.ChangeAsync("emulator_refresh_bucket", BucketArguments());
        await http.ChangeAsync("emulator_install_bundle", PackageArguments(first), stdio);
        Assert.Null(await SelectedPinAsync(http, rootId));
        await http.ChangeAsync("emulator_select_bundle", ProjectArguments(rootId, first), stdio);
        Assert.Equal(first, await SelectedPinAsync(http, rootId));

        var next = await PublishPackageAsync(root, git, NextPin);
        await http.ChangeAsync("emulator_refresh_bucket", BucketArguments(), stdio);
        await http.ChangeAsync("emulator_update_bundle", new(StringComparer.Ordinal) { ["current"] = first, ["target"] = next });
        Assert.Equal(first, await SelectedPinAsync(http, rootId));
        Assert.Equal(2, (await http.CallAsync("emulator_list_installed")).GetProperty("data").GetArrayLength());
        await http.ChangeAsync("emulator_select_bundle", ProjectArguments(rootId, next));

        var store = new EmulatorBundleStore(Path.Combine(EmulatorData(root), "emulators"));
        using var running = Assert.IsType<EmulatorBundleStore.BundleLease>(store.Acquire(next, RuntimeInformation.RuntimeIdentifier, AdapterApi));
        await http.ChangeAsync("emulator_rollback_bundle", ProjectArguments(rootId, first), stdio);
        Assert.Equal(first, await SelectedPinAsync(http, rootId));
        Assert.Equal(next, running.Installation.Receipt.Pin);
        await AssertRemovalBlockedAsync(http, stdio, next);
        running.Dispose();
        await http.ChangeAsync("emulator_remove_bundle", PackageArguments(next), stdio);
        await http.ChangeAsync("emulator_remove_bucket", BucketArguments());
        var retained = Assert.Single((await http.CallAsync("emulator_list_installed")).GetProperty("data").EnumerateArray());
        Assert.Equal(first, retained.GetProperty("pin").Deserialize<Pin>(RetroIde.Application.WireJson.Options));
        Assert.Equal(first, await SelectedPinAsync(http, rootId));
    }

    /// <remarks>TEST-RETRO-015#ac-test-mcp-scope.</remarks>
    [AvaloniaFact]
    public async Task Package_selection_requires_a_project_in_the_attached_workspace()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        await using var session = OpenEmulatorWorkbench(root);
        await using var http = await Connection.ConnectAsync(session, token);
        var folder = await http.AddRootAsync(root.Directory.Replace('\\', '/'));
        var context = (await http.CallAsync("workbench_get_context")).GetProperty("context");
        foreach (var rootId in new[] { folder, Guid.NewGuid() })
        {
            var args = ProjectArguments(rootId, FirstPin);
            args["context"] = context;
            var result = await http.CallAsync("emulator_select_bundle", args);
            Assert.Equal("emulator_project_missing", result.GetProperty("code").GetString(), StringComparer.Ordinal);
        }
        Assert.False(File.Exists(Path.Combine(root.Directory, ".retroide/emulator.json")));
    }

    private static async Task<Pin> PublishPackageAsync(BundleStoreFixture root, GitWorktreeFixture git, Pin pin)
    {
        using var zip = Archive(("adapter/Uae.dll", 0), ("bin/uae", 0x81ed), ("bin/uae.exe", 0), ("profiles/amiga-aga.json", 0));
        var artifact = Path.Combine(root.Directory, pin.Version + ".zip");
        await File.WriteAllBytesAsync(artifact, zip.ToArray(), root.CancellationToken).ConfigureAwait(false);
        var url = new Uri(artifact).AbsoluteUri;
        var bytes = WithArtifact(ReleaseManifest(pin, Digest(zip)), url);
        var manifest = Encoding.UTF8.GetString(bytes)
            .Replace("https://example.invalid/uae-win-x64.zip", url, StringComparison.Ordinal)
            .Replace(WindowsHash, Digest(zip), StringComparison.Ordinal);
        var revision = await PublishManifestAsync(git, Encoding.UTF8.GetBytes(manifest), root.CancellationToken).ConfigureAwait(false);
        return pin with { ManifestRevision = revision };
    }

    private static Dictionary<string, object?> PackageArguments(Pin pin) => new(StringComparer.Ordinal) { ["pin"] = pin };

    private static Dictionary<string, object?> ProjectArguments(Guid root, Pin pin) =>
        new(StringComparer.Ordinal) { ["rootId"] = root, ["pin"] = pin };

    private static async Task<Pin?> SelectedPinAsync(Connection http, Guid root)
    {
        var response = await http.CallAsync("emulator_get_project_bundle", new(StringComparer.Ordinal) { ["rootId"] = root }).ConfigureAwait(true);
        Assert.Equal("ok", response.GetProperty("code").GetString(), StringComparer.Ordinal);
        var selection = response.GetProperty("data").GetProperty("selection");
        return selection.ValueKind == JsonValueKind.Null ? null :
            selection.GetProperty("active").Deserialize<Pin>(RetroIde.Application.WireJson.Options);
    }

    private static async Task AssertRemovalBlockedAsync(Connection http, McpClient stdio, Pin pin)
    {
        var args = PackageArguments(pin);
        args["context"] = (await http.CallAsync("workbench_get_context").ConfigureAwait(true)).GetProperty("context");
        var result = await http.CallAsync("emulator_remove_bundle", args, stdio).ConfigureAwait(true);
        Assert.Equal("emulator_bundle_in_use", result.GetProperty("code").GetString(), StringComparer.Ordinal);
    }
}
