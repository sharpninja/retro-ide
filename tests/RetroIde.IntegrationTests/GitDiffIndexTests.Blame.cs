using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Traces exact source lines across Git renames, worktrees, disk edits, and editor drafts.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Blame_attributes_renamed_lines_and_preserves_disk_buffer_and_checkout_identity()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        const string Original = GitWorktreeFixture.ChangedPath;
        const string Renamed = "src/Renamed café [demo].cs";
        GitWorktreeFixture.Write(sample.Primary, Original, "original first\noriginal second\n");
        await sample.GitAsync(sample.Primary, token, "add", "--", Original).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "commit", "-m", "Original attribution").ConfigureAwait(true);
        var originalId = (await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await sample.GitAsync(sample.Primary, token, "mv", "--", Original, Renamed).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "commit", "-m", "Rename source").ConfigureAwait(true);
        GitWorktreeFixture.Write(sample.Primary, Renamed, "original first\nworking second\n");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "blame-stdio.json")).ConfigureAwait(true);
        var root = await connection.AddRootAsync(sample.Primary).ConfigureAwait(true);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        }).ConfigureAwait(true);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = Renamed,
        }).ConfigureAwait(true);
        var editor = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        editor.Buffer.Text = "editor draft, not a saved line";

        await connection.ExpandAsync(root, "").ConfigureAwait(true);
        await connection.ExpandAsync(root, "src").ConfigureAwait(true);
        await connection.ChangeAsync("workspace_select_entry", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = GitWorktreeFixture.HunkPath,
        }).ConfigureAwait(true);
        using (var materialization = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            materialization.CancelAfter(TimeSpan.FromSeconds(8));
            while (!session.SourceControl.OpenBlameCommand.CanExecute(null))
                await Task.Delay(20, materialization.Token).ConfigureAwait(true);
        }
        await session.SourceControl.OpenBlameCommand.ExecuteAsync(null).ConfigureAwait(true);
        var cleanTab = Assert.IsType<JsonElement>(session.SourceControl.LastResult?.Data);
        Assert.Equal(GitWorktreeFixture.HunkPath, cleanTab.GetProperty("blame").GetProperty("source").GetProperty("path").GetString());
        await connection.ChangeAsync("git_close_blame", new(StringComparer.Ordinal)
        {
            ["viewKey"] = cleanTab.GetProperty("viewKey").GetGuid(),
        }).ConfigureAwait(true);

        var working = await connection.CallAsync("git_get_blame", Input(), stdio).ConfigureAwait(true);
        Assert.Equal("ok", Code(working));
        var lines = working.GetProperty("data").GetProperty("lines").EnumerateArray().ToArray();
        Assert.Collection(lines, first =>
        {
            Assert.Equal(1, first.GetProperty("lineNumber").GetInt32());
            Assert.Equal(originalId, first.GetProperty("commitId").GetString());
            Assert.Equal(Original, first.GetProperty("originPath").GetString());
            Assert.Equal("original first", first.GetProperty("text").GetString());
        }, second =>
        {
            Assert.True(second.GetProperty("uncommitted").GetBoolean());
            Assert.Equal("working second", second.GetProperty("text").GetString());
        });
        Assert.Equal("editor draft, not a saved line", editor.Buffer.Text);

        var historical = Input(Original);
        historical["commitId"] = originalId;
        historical["firstLine"] = 2;
        historical["lastLine"] = 2;
        var historicalResult = await connection.CallAsync("git_get_blame", historical).ConfigureAwait(true);
        Assert.Equal("ok", Code(historicalResult));
        var oldLine = Assert.Single(historicalResult.GetProperty("data").GetProperty("lines").EnumerateArray());
        Assert.Equal(2, oldLine.GetProperty("lineNumber").GetInt32());
        Assert.Equal(originalId, oldLine.GetProperty("commitId").GetString());
        Assert.Equal("original second", oldLine.GetProperty("text").GetString());

        var firstTab = await connection.ChangeAsync("git_open_blame", Input()).ConfigureAwait(true);
        var tabId = firstTab.GetProperty("data").GetProperty("viewKey").GetGuid();
        var repeated = await connection.ChangeAsync("git_open_blame", Input(), stdio).ConfigureAwait(true);
        Assert.Equal(tabId, repeated.GetProperty("data").GetProperty("viewKey").GetGuid());
        await connection.ChangeAsync("git_close_blame", new(StringComparer.Ordinal) { ["viewKey"] = tabId }).ConfigureAwait(true);
        var other = Input();
        other["worktreePath"] = sample.Linked;
        Assert.Equal("git_worktree_mismatch", Code(await connection.CallAsync("git_get_blame", other).ConfigureAwait(true)));
        Assert.Equal("git_scope_invalid", Code(await connection.CallAsync("git_get_blame", Input("../outside.txt")).ConfigureAwait(true)));
        Assert.Equal("original first\nworking second\n", await File.ReadAllTextAsync(Path.Combine(sample.Primary, Renamed), token).ConfigureAwait(true));

        Dictionary<string, object?> Input(string path = Renamed) => new(StringComparer.Ordinal)
        {
            ["worktreePath"] = sample.Primary, ["path"] = path,
        };
        static string? Code(JsonElement result) => result.GetProperty("code").GetString();
    }
}
