using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Git_submodule_commands_scope_updates_and_preserve_local_source()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        const string ModulePath = "deps/Café [lib]";
        const string ModuleName = "local.library";
        const string OtherPath = "deps/other library";
        var folder = Path.Combine(sample.Primary, ModulePath);
        await AddModuleAsync(ModuleName, ModulePath).ConfigureAwait(true);
        await AddModuleAsync("other", OtherPath).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgSign=false", "commit", "-m", "Pin local submodules").ConfigureAwait(true);
        var initial = (await sample.GitAsync(folder, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await sample.GitAsync(sample.Primary, token, "submodule", "deinit", "--", ModulePath, OtherPath).ConfigureAwait(true);

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "submodule-stdio.json")).ConfigureAwait(true);
        var root = await connection.AddRootAsync(sample.Primary).ConfigureAwait(true);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" }).ConfigureAwait(true);
        var port = new SubmodulePort(connection, stdio, sample.Primary);
        var listed = await port.ListAsync().ConfigureAwait(true);
        var original = FindModule(listed, ModulePath);
        Assert.Equal(ModuleName, original.GetProperty("name").GetString());
        Assert.Equal(initial, original.GetProperty("indexCommit").GetString());
        Assert.False(original.GetProperty("isConfigured").GetBoolean());
        Assert.False(original.GetProperty("hasCheckout").GetBoolean());
        Assert.Equal("git_submodule_unavailable", SubmoduleCode(await port.ChangeAsync("git_init_submodule", original, "../outside").ConfigureAwait(true)));

        var initialized = await port.ChangeAsync("git_init_submodule", original, relay: stdio).ConfigureAwait(true);
        Assert.Equal("ok", SubmoduleCode(initialized));
        var configured = initialized.GetProperty("data");
        Assert.True(configured.GetProperty("isConfigured").GetBoolean());
        Assert.False(configured.GetProperty("hasCheckout").GetBoolean());
        Assert.Equal("git_submodule_changed", SubmoduleCode(await port.ChangeAsync("git_update_submodule", original).ConfigureAwait(true)));
        Assert.False(FindModule(await port.ListAsync().ConfigureAwait(true), OtherPath).GetProperty("isConfigured").GetBoolean());
        var installed = await port.ChangeAsync("git_update_submodule", configured).ConfigureAwait(true);
        Assert.Equal("ok", SubmoduleCode(installed));
        Assert.Equal(initial, installed.GetProperty("data").GetProperty("head").GetString());
        Assert.True(File.Exists(Path.Combine(folder, GitWorktreeFixture.ChangedPath)));
        Assert.False(File.Exists(Path.Combine(sample.Primary, OtherPath, ".git")));

        await sample.GitAsync(sample.Primary, token, "config", "--file", ".gitmodules", $"submodule.{ModuleName}.url", sample.Peer).ConfigureAwait(true);
        Assert.Equal("git_submodule_changed", SubmoduleCode(await port.ChangeAsync("git_sync_submodule", installed.GetProperty("data")).ConfigureAwait(true)));
        var reviewSync = FindModule(await port.ListAsync().ConfigureAwait(true), ModulePath);
        var synced = await port.ChangeAsync("git_sync_submodule", reviewSync, relay: stdio).ConfigureAwait(true);
        Assert.Equal("ok", SubmoduleCode(synced));
        var configuredUrl = await sample.GitAsync(sample.Primary, token, "config", "--local", "--get", $"submodule.{ModuleName}.url").ConfigureAwait(true);
        Assert.Equal(sample.Peer, configuredUrl.Trim().Replace('/', Path.DirectorySeparatorChar), NativeSubmodulePaths);

        GitWorktreeFixture.Write(folder, GitWorktreeFixture.ChangedPath, "second submodule commit\n");
        await sample.GitAsync(folder, token, "add", "--", GitWorktreeFixture.ChangedPath).ConfigureAwait(true);
        await sample.GitAsync(folder, token, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgSign=false", "commit", "-m", "second submodule commit").ConfigureAwait(true);
        var later = (await sample.GitAsync(folder, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await sample.GitAsync(folder, token, "branch", "fixture-pin", later).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "update-index", "--cacheinfo", "160000", later, ModulePath).ConfigureAwait(true);
        await sample.GitAsync(folder, token, "checkout", "--detach", initial).ConfigureAwait(true);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = $"{ModulePath}/{GitWorktreeFixture.ChangedPath}" }).ConfigureAwait(true);
        var editor = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var previousText = editor.Buffer.Text;
        await EditAsync("unsaved submodule buffer").ConfigureAwait(true);
        var beforeUpdate = FindModule(await port.ListAsync().ConfigureAwait(true), ModulePath);
        Assert.Equal(later, beforeUpdate.GetProperty("indexCommit").GetString());
        Assert.Equal(initial, beforeUpdate.GetProperty("head").GetString());
        Assert.Equal("git_submodule_editors_busy", SubmoduleCode(await port.ChangeAsync("git_update_submodule", beforeUpdate).ConfigureAwait(true)));
        Assert.Equal("unsaved submodule buffer", editor.Buffer.Text);
        await EditAsync(previousText).ConfigureAwait(true);

        GitWorktreeFixture.Write(folder, GitWorktreeFixture.ChangedPath, "working submodule changes\n");
        var dirty = FindModule(await port.ListAsync().ConfigureAwait(true), ModulePath);
        Assert.True(dirty.GetProperty("isDirty").GetBoolean());
        Assert.Equal("git_submodule_dirty", SubmoduleCode(await port.ChangeAsync("git_update_submodule", dirty).ConfigureAwait(true)));
        await sample.GitAsync(folder, token, "restore", "--worktree", "--", GitWorktreeFixture.ChangedPath).ConfigureAwait(true);
        var clean = FindModule(await port.ListAsync().ConfigureAwait(true), ModulePath);
        var updated = await port.ChangeAsync("git_update_submodule", clean, relay: stdio).ConfigureAwait(true);
        Assert.Equal("ok", SubmoduleCode(updated));
        Assert.Equal(later, updated.GetProperty("data").GetProperty("head").GetString());
        Assert.Equal("second submodule commit\n", editor.Buffer.Text.ReplaceLineEndings("\n"));

        Task<string> AddModuleAsync(string name, string path) =>
            sample.GitAsync(sample.Primary, token, "-c", "protocol.file.allow=always", "submodule", "add", "--name", name, "--", sample.Remote, path);
        async Task EditAsync(string text) =>
            await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = editor.Capture(), ["offset"] = 0, ["length"] = editor.Buffer.TextLength, ["text"] = text,
            }).ConfigureAwait(false);
    }

    private static StringComparer NativeSubmodulePaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string? SubmoduleCode(JsonElement reply) => reply.GetProperty("code").GetString();
    private static JsonElement FindModule(JsonElement rows, string path) =>
        Assert.Single(rows.EnumerateArray(), row => string.Equals(row.GetProperty("path").GetString(), path, StringComparison.Ordinal));

    private sealed class SubmodulePort(GitWorkspaceStateTests.WorkspaceConnection connection, McpClient stdio, string path)
    {
        internal async Task<JsonElement> ListAsync()
        {
            var result = await connection.CallAsync("git_list_submodules", new(StringComparer.Ordinal) { ["worktreePath"] = path }, stdio)
                .ConfigureAwait(false);
            Assert.Equal("ok", SubmoduleCode(result));
            return result.GetProperty("data");
        }

        internal async Task<JsonElement> ChangeAsync(string tool, JsonElement reviewed, string? overridePath = null, McpClient? relay = null)
        {
            var context = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            return await connection.CallAsync(tool, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = path, ["path"] = overridePath ?? reviewed.GetProperty("path").GetString(),
                ["expectedRevision"] = reviewed.GetProperty("revision").GetString(), ["context"] = context,
            }, relay).ConfigureAwait(false);
        }
    }
}
