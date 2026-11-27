using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <remarks>FR-RETRO-009#ac-emulators-catalog; TEST-RETRO-009#ac-test-bucket-platforms; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Emulator_bucket_tools_share_one_catalog_across_transports_and_keep_the_last_verified_commit()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        using var git = await GitWorktreeFixture.CreateAsync("git", token);
        var revision = await PublishManifestAsync(git, ReleaseManifest(FirstPin, LinuxHash), token);
        var location = new Uri(git.Remote).AbsoluteUri;
        await using (var session = OpenEmulatorWorkbench(root))
        {
            await using var http = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
            await using var stdio = await http.ConnectStdioAsync(Path.Combine(root.Directory, "emulators-stdio.json"), shareGrant: true);
            var tools = await stdio.ListToolsAsync(cancellationToken: token);
            var configure = Assert.Single(tools, tool => string.Equals(tool.Name, "emulator_configure_bucket", StringComparison.Ordinal));
            var context = configure.JsonSchema.GetProperty("properties").GetProperty("context");
            Assert.Equal(session.Registry.Context.Schema.GetRawText(), context.GetRawText(), StringComparer.Ordinal);

            Assert.Empty((await http.CallAsync("emulator_list_buckets")).GetProperty("data").GetProperty("buckets").EnumerateArray());
            await http.ChangeAsync("emulator_configure_bucket", BucketArguments(location), stdio);
            await RefreshWithHandleAsync(http, stdio, revision, token);
            await AssertCatalogAsync(http, revision);
            var stdioState = await http.CallAsync("emulator_list_buckets", transport: stdio);
            Assert.Equal(revision, Assert.Single(stdioState.GetProperty("data").GetProperty("buckets").EnumerateArray())
                .GetProperty("revision").GetString(), StringComparer.Ordinal);

            Directory.Move(git.Remote, git.Remote + ".offline");
            var captured = await http.CallAsync("workbench_get_context");
            var args = BucketArguments();
            args["context"] = captured.GetProperty("context");
            var failed = await http.CallAsync("emulator_refresh_bucket", args);
            Assert.Equal("emulator_bucket_io", failed.GetProperty("code").GetString(), StringComparer.Ordinal);
            await AssertCatalogAsync(http, revision);
        }

        await using var reopened = OpenEmulatorWorkbench(root);
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(reopened, token);
        await AssertCatalogAsync(connection, revision);
        await connection.ChangeAsync("emulator_remove_bucket", BucketArguments());
        Assert.Empty((await connection.CallAsync("emulator_list_buckets")).GetProperty("data").GetProperty("buckets").EnumerateArray());
    }

    /// <remarks>TEST-RETRO-015#ac-test-mcp-scope.</remarks>
    [AvaloniaFact]
    public async Task Unauthorized_or_stale_bucket_changes_cannot_change_shared_configuration()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        await using var session = OpenEmulatorWorkbench(root);
        await using var owner = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var observer = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token, canMutate: false);
        var context = (await owner.CallAsync("workbench_get_context")).GetProperty("context");
        var args = BucketArguments("https://example.invalid/fixture.git");
        args["context"] = context;
        var denied = await observer.CallAsync("emulator_configure_bucket", args);
        Assert.Equal("forbidden", denied.GetProperty("code").GetString(), StringComparer.Ordinal);

        await owner.ChangeAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "output" });
        var stale = await owner.CallAsync("emulator_configure_bucket", args);
        Assert.Equal("stale_revision", stale.GetProperty("code").GetString(), StringComparer.Ordinal);
        Assert.Empty((await owner.CallAsync("emulator_list_buckets")).GetProperty("data").GetProperty("buckets").EnumerateArray());
    }

    private static WorkbenchSession OpenEmulatorWorkbench(BundleStoreFixture root) =>
        new(new WorkspaceExplorerStore(Path.Combine(root.Directory, "workspace.json")), savedLayout: null,
            localization: null, logs: null,
            emulatorDataFolder: EmulatorData(root));

    private static string EmulatorData(BundleStoreFixture root) =>
        Path.Combine(root.Directory, "workspace application data with a deliberately long path");

    private static Dictionary<string, object?> BucketArguments(string? repository = null)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["bucketId"] = "official" };
        if (repository is not null)
            arguments.Add("repository", repository);
        return arguments;
    }

    private static async Task AssertCatalogAsync(GitWorkspaceStateTests.WorkspaceConnection connection, string revision)
    {
        var response = await connection.CallAsync("emulator_list_buckets").ConfigureAwait(true);
        Assert.Equal("ok", response.GetProperty("code").GetString(), StringComparer.Ordinal);
        var bucket = Assert.Single(response.GetProperty("data").GetProperty("buckets").EnumerateArray());
        Assert.Equal("official", bucket.GetProperty("id").GetString(), StringComparer.Ordinal);
        Assert.Equal(revision, bucket.GetProperty("revision").GetString(), StringComparer.Ordinal);
        var release = Assert.Single(bucket.GetProperty("releases").EnumerateArray());
        Assert.Equal(revision, release.GetProperty("pin").GetProperty("manifestRevision").GetString(), StringComparer.Ordinal);
        Assert.Equal(JsonValueKind.True, release.GetProperty("isCompatible").ValueKind);
    }

    private static async Task RefreshWithHandleAsync(GitWorkspaceStateTests.WorkspaceConnection http, McpClient stdio, string revision,
        CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var args = BucketArguments();
        args["background"] = true;
        var started = await http.ChangeAsync("emulator_refresh_bucket", args).ConfigureAwait(true);
        var operation = started.GetProperty("data").GetProperty("operationId").GetGuid();
        var handle = new Dictionary<string, object?>(StringComparer.Ordinal) { ["operationId"] = operation };
        while (true)
        {
            var result = await http.CallAsync("workbench_operation_result", handle, stdio).ConfigureAwait(true);
            var code = result.GetProperty("code").GetString();
            if (!string.Equals(code, "operation_pending", StringComparison.Ordinal))
            {
                Assert.Equal("ok", code, StringComparer.Ordinal);
                var original = result.GetProperty("data");
                Assert.Equal("ok", original.GetProperty("code").GetString(), StringComparer.Ordinal);
                Assert.Equal(operation, original.GetProperty("operationId").GetGuid());
                Assert.Equal(revision, original.GetProperty("data").GetProperty("revision").GetString(), StringComparer.Ordinal);
                return;
            }
            await Task.Delay(25, deadline.Token).ConfigureAwait(true);
        }
    }
}
