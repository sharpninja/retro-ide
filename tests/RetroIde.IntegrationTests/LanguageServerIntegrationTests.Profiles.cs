using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Restores named profiles and stopped state from a normal workbench close.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaFact]
    public async Task Named_language_profiles_survive_a_workbench_restart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-language-profiles-");
        try
        {
            var store = new WorkspaceExplorerStore(Path.Combine(fixture.FullName, "workspace.json"));
            await File.WriteAllTextAsync(Path.Combine(fixture.FullName, "fixture.c"), "int c;\n", token);
            await File.WriteAllTextAsync(Path.Combine(fixture.FullName, "fixture.asm"), "lda #1\n", token);
            Guid rootId;
            await using (var before = new WorkbenchSession(store, null))
            {
                var window = new WorkbenchWindow(before);
                window.Show();
                await using var client = await Connection.ConnectAsync(before, token);
                rootId = await client.AddRootAsync(fixture.FullName);
                await ConfigureAsync(client, rootId, profileId: "c-tools");
                await ConfigureAsync(client, rootId, encoding: "utf-32", profileId: "asm-tools", extension: ".asm");
                var conflicting = ConfigurationArguments(rootId, profileId: "overlap", extension: ".C");
                conflicting["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
                var rejected = await client.CallAsync("language_configure_server", conflicting);
                Assert.Equal("language_profile_conflict", Code(rejected));
                var list = await client.CallAsync("language_list_servers");
                Assert.Equal(2, list.GetProperty("data").GetArrayLength());
                var alternate = await OpenSourceAsync(client, rootId, "fixture.asm");
                var response = await CompleteAsync(client, alternate, 3);
                Assert.Equal("asm-tools", response.GetProperty("server").GetProperty("profileId").GetString());
                Assert.Equal("utf-32", response.GetProperty("server").GetProperty("positionEncoding").GetString());
                var ambiguous = await client.CallAsync("language_get_server_output", new(StringComparer.Ordinal) { ["rootId"] = rootId });
                Assert.Equal("language_profile_required", Code(ambiguous));
                await client.ChangeAsync("language_stop_server", Profile(rootId, "asm-tools"));
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                window.Close();
                await closed.Task.WaitAsync(token);
            }

            await using var reopened = new WorkbenchSession(store, null);
            await using var attached = await Connection.ConnectAsync(reopened, token);
            var c = await OpenAsync(attached, rootId);
            await WaitForProfileAsync(attached, "c-tools", "running", token);
            await WaitForProfileAsync(attached, "asm-tools", "stopped", token);
            AssertFixtureReply(await CompleteAsync(attached, c, 4), "int c;\n", 4, 1);
            var asm = await OpenSourceAsync(attached, rootId, "fixture.asm");
            var unavailable = await attached.CallAsync("language_complete", new(StringComparer.Ordinal) { ["document"] = asm, ["offset"] = 3 });
            Assert.Equal("language_server_unavailable", Code(unavailable));
            await attached.ChangeAsync("language_restart_server", Profile(rootId, "asm-tools"));
            AssertFixtureReply(await CompleteAsync(attached, asm, 3), "lda #1\n", 3, 1);
            await attached.ChangeAsync("language_remove_server", Profile(rootId, "asm-tools"));
            Assert.Single(reopened.Explorer.Snapshot().LanguageProfiles!);
            var profiles = await attached.CallAsync("language_list_servers");
            Assert.Single(profiles.GetProperty("data").EnumerateArray());
        }
        finally { fixture.Delete(recursive: true); }
    }

    /// <summary>Preserves routing across compatible roots and prunes a root removed by the Explorer.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaFact]
    public async Task Changed_settings_isolate_roots_and_workspace_removal_releases_membership()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var temp = Directory.CreateTempSubdirectory("retro-ide-language-profile-isolation-");
        try
        {
            var dirs = new[] { "a", "b", "c" }.Select(name => Directory.CreateDirectory(Path.Combine(temp.FullName, name)).FullName).ToArray();
            await File.WriteAllTextAsync(Path.Combine(dirs[0], "fixture.c"), "int kept;\n", token);
            await using var workbench = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(workbench, token);
            var first = await client.AddRootAsync(dirs[0]);
            var second = await client.AddRootAsync(dirs[1]);
            var third = await client.AddRootAsync(dirs[2]);
            var dynamicFolders = new { workspaceFolderMode = "dynamic" };
            await ConfigureAsync(client, first, dynamicFolders, settings: new { language = "c", standard = "c99" });
            await ConfigureAsync(client, second, dynamicFolders, settings: new { standard = "c99", language = "c" });
            await ConfigureAsync(client, third, dynamicFolders, settings: new { language = "c", standard = "c23" });
            var rows = (await client.CallAsync("language_list_servers")).GetProperty("data").EnumerateArray()
                .ToDictionary(row => row.GetProperty("rootId").GetGuid(), row => row.GetProperty("sessionId").GetGuid());
            Assert.Equal(rows[first], rows[second]);
            Assert.NotEqual(rows[first], rows[third]);
            var doc = await OpenAsync(client, first);
            Assert.Equal(2, ReportedFolders(await CompleteAsync(client, doc, 4)).GetArrayLength());

            Assert.True(workbench.Explorer.RemoveRootsUnder(dirs[1]));
            workbench.Controller.LanguageServers.ReconcileRoots();
            while (true)
            {
                var result = await CompleteAsync(client, doc, 4);
                Assert.Equal(rows[first], result.GetProperty("server").GetProperty("sessionId").GetGuid());
                var folders = ReportedFolders(result);
                if (folders.GetArrayLength() == 1)
                {
                    Assert.Equal(new Uri(dirs[0]).AbsoluteUri, folders[0].GetString());
                    break;
                }
                await Task.Delay(25, token);
            }
            Assert.Equal(2, workbench.Explorer.Snapshot().LanguageProfiles!.Length);
        }
        finally { temp.Delete(recursive: true); }
    }

    /// <summary>Joins compatible roots only when the server can represent the assigned workspace folders.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-session; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaTheory]
    [InlineData("unsupported")]
    [InlineData("initial")]
    [InlineData("dynamic")]
    public async Task Workspace_folder_capabilities_control_process_sharing_and_detachment(string capability)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("retro-ide-language-roots-");
        try
        {
            var paths = new[] { "one", "two" }.Select(name => Directory.CreateDirectory(Path.Combine(folder.FullName, name)).FullName).ToArray();
            await File.WriteAllTextAsync(Path.Combine(paths[0], "fixture.c"), "int one;\n", token);
            await File.WriteAllTextAsync(Path.Combine(paths[1], "fixture.c"), "int two;\n", token);
            await using var workbench = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(workbench, token);
            var first = await client.AddRootAsync(paths[0]);
            var second = await client.AddRootAsync(paths[1]);
            var docOne = await OpenAsync(client, first);
            var docTwo = await OpenAsync(client, second);
            var options = new { workspaceFolderMode = capability };
            await ConfigureAsync(client, first, options);
            var old = await CompleteAsync(client, docOne, 4);
            var formerSession = old.GetProperty("server").GetProperty("sessionId").GetGuid();

            await ConfigureAsync(client, second, options);
            var resultOne = await CompleteAsync(client, docOne, 4);
            var resultTwo = await CompleteAsync(client, docTwo, 4);
            AssertFixtureReply(resultOne, "int one;\n", 4, 1);
            AssertFixtureReply(resultTwo, "int two;\n", 4, 1);
            Assert.Equal(first, resultOne.GetProperty("server").GetProperty("rootId").GetGuid());
            Assert.Equal(second, resultTwo.GetProperty("server").GetProperty("rootId").GetGuid());
            var sessionOne = resultOne.GetProperty("server").GetProperty("sessionId").GetGuid();
            var sessionTwo = resultTwo.GetProperty("server").GetProperty("sessionId").GetGuid();
            Assert.Equal(capability != "unsupported", sessionOne == sessionTwo);
            Assert.Equal(capability != "initial", formerSession == sessionOne);
            Assert.Equal(capability == "unsupported" ? 1 : 2, ReportedFolders(resultOne).GetArrayLength());

            await client.ChangeAsync("language_stop_server", new(StringComparer.Ordinal) { ["rootId"] = first });
            var after = await CompleteAsync(client, docTwo, 4);
            Assert.Equal(capability != "initial", sessionTwo == after.GetProperty("server").GetProperty("sessionId").GetGuid());
            Assert.Equal(new Uri(paths[1]).AbsoluteUri, Assert.Single(ReportedFolders(after).EnumerateArray()).GetString());
        }
        finally { folder.Delete(recursive: true); }
    }

    private static Dictionary<string, object?> Profile(Guid rootId, string profileId) =>
        new(StringComparer.Ordinal) { ["rootId"] = rootId, ["profileId"] = profileId };

    private static JsonElement ReportedFolders(JsonElement result) =>
        result.GetProperty("result")[0].GetProperty("data").GetProperty("workspaceFolders");

    private static async Task WaitForProfileAsync(Connection client, string profileId, string state, CancellationToken token)
    {
        while (true)
        {
            var reply = await client.CallAsync("language_list_servers");
            var profile = Assert.Single(reply.GetProperty("data").EnumerateArray()
                .Where(item => string.Equals(item.GetProperty("profileId").GetString(), profileId, StringComparison.Ordinal)));
            var actual = profile.GetProperty("state").GetString();
            if (string.Equals(state, actual, StringComparison.Ordinal))
                return;
            Assert.NotEqual("faulted", actual);
            await Task.Delay(25, token);
        }
    }

    private static async Task<JsonElement> OpenSourceAsync(Connection client, Guid rootId, string filename) =>
        (await client.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = rootId, ["relativePath"] = filename })).GetProperty("data");
}
