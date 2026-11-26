using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Reads real refs, linked checkout state, merge ancestry, and literal change paths over MCP.</summary>
    /// <remarks>TEST-RETRO-013; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Workbench_reads_pinned_Git_history_and_merge_details()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var checkout = sample.Primary;
        await sample.GitAsync(checkout, token, "switch", "-c", "feature/history");
        GitWorktreeFixture.Write(checkout, GitWorktreeFixture.ChangedPath, "history feature file\n");
        await sample.GitAsync(checkout, token, "--literal-pathspecs", "add", "--", GitWorktreeFixture.ChangedPath);
        await sample.GitAsync(checkout, token, "commit", "-m", "Feature history entry");
        var feature = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "tag", "-a", "review-v1", "-m", "Reviewed");
        await sample.GitAsync(checkout, token, "switch", "main");
        GitWorktreeFixture.Write(checkout, GitWorktreeFixture.ConflictPath, "history main file\n");
        await sample.GitAsync(checkout, token, "add", "--", GitWorktreeFixture.ConflictPath);
        await sample.GitAsync(checkout, token, "commit", "-m", "Main history entry");
        var main = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "merge", "--no-ff", "-m", "Merge history feature", "feature/history");
        var merged = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "history-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });

        var refs = await ReadAsync("git_list_refs");
        var local = Assert.Single(refs.GetProperty("refs").EnumerateArray(), entry => HasName(entry, "refs/heads/main"));
        Assert.Equal("refs/remotes/origin/main", local.GetProperty("upstream").GetString());
        var tag = Assert.Single(refs.GetProperty("refs").EnumerateArray(), entry => HasName(entry, "refs/tags/review-v1"));
        Assert.Equal(feature, tag.GetProperty("peeledObjectId").GetString());
        Assert.Contains(refs.GetProperty("worktrees").EnumerateArray(), tree =>
            string.Equals(sample.Linked, tree.GetProperty("path").GetString(), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        var page = await ReadAsync("git_get_history", new(StringComparer.Ordinal) { ["limit"] = 1 });
        Assert.Equal(merged, page.GetProperty("anchor").GetString());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var merge = Assert.Single(page.GetProperty("commits").EnumerateArray());
        Assert.Equal(merged, merge.GetProperty("id").GetString());
        Assert.Equal([main, feature], merge.GetProperty("parents").EnumerateArray().Select(id => id.GetString()));
        await sample.GitAsync(checkout, token, "commit", "--allow-empty", "-m", "Outside update");
        var older = await ReadAsync("git_get_history", new(StringComparer.Ordinal) { ["anchor"] = merged, ["offset"] = 1 });
        Assert.DoesNotContain(older.GetProperty("commits").EnumerateArray(), node => string.Equals(merged, node.GetProperty("id").GetString(), StringComparison.Ordinal));
        Assert.Contains(older.GetProperty("commits").EnumerateArray(), node => string.Equals(feature, node.GetProperty("id").GetString(), StringComparison.Ordinal));

        var details = await ReadAsync("git_get_commit_details", new(StringComparer.Ordinal) { ["commitId"] = merged });
        Assert.Equal(main, details.GetProperty("comparedTo").GetString());
        Assert.Equal(GitWorktreeFixture.ChangedPath, Assert.Single(details.GetProperty("changes").EnumerateArray()).GetProperty("path").GetString());
        Assert.Contains("Merge history feature", details.GetProperty("message").GetString(), StringComparison.Ordinal);
        var relative = await connection.CallAsync("git_get_commit_details", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["commitId"] = "HEAD~1",
        });
        Assert.Equal("git_commit_id_invalid", relative.GetProperty("code").GetString());
        var firstParent = await ReadAsync("git_get_history", new(StringComparer.Ordinal) { ["refName"] = "refs/tags/review-v1" });
        Assert.Equal(feature, firstParent.GetProperty("anchor").GetString());
        await VerifyHistoryViewAsync(session, connection, stdio, checkout, merged, token);

        async Task<JsonElement> ReadAsync(string tool, Dictionary<string, object?>? args = null)
        {
            args ??= new(StringComparer.Ordinal);
            args["worktreePath"] = checkout;
            var response = await connection.CallAsync(tool, args, stdio).ConfigureAwait(false);
            Assert.Equal("ok", response.GetProperty("code").GetString());
            return response.GetProperty("data");
        }

        static bool HasName(JsonElement value, string name) =>
            string.Equals(name, value.GetProperty("name").GetString(), StringComparison.Ordinal);
    }

    private static async Task VerifyHistoryViewAsync(WorkbenchSession session, GitWorkspaceStateTests.WorkspaceConnection connection,
        McpClient stdio, string worktree, string mergeId, CancellationToken token)
    {
        var window = new WorkbenchWindow(session) { Width = 1360, Height = 900 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var menu = Assert.Single(window.GetVisualDescendants().OfType<MenuItem>(), item => HasId(item, "menu.git"));
            var open = Assert.Single(Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource), item => HasId(item, "git.history.open.menu"));
            var action = Assert.IsAssignableFrom<IAsyncRelayCommand>(open.Command);
            await action.ExecuteAsync(null).WaitAsync(token);
            Assert.Equal("ok", session.SourceControl.LastResult?.Code);
            var tab = Assert.IsType<string>(session.Layout.SelectedDocumentId);
            Assert.DoesNotContain(tab, JsonSerializer.Serialize(session.Layout.Capture()), StringComparison.Ordinal);

            var viaAgent = await connection.ChangeAsync("git_open_history", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = worktree, ["anchor"] = mergeId, ["limit"] = 4,
            }, stdio);
            Assert.Equal(tab, session.Layout.SelectedDocumentId);
            var graph = await WaitAsync<ListBox>("git.history.graph");
            var merge = Assert.Single(Assert.IsAssignableFrom<IEnumerable<GitHistoryGraph.Row>>(graph.ItemsSource),
                row => string.Equals(row.Commit.Id, mergeId, StringComparison.Ordinal));
            Assert.Equal(2, merge.Parents.Distinct().Count());
            graph.SelectedItem = merge;
            var inspector = await WaitAsync<Button>("git.history.commit-details.toolbar");
            var inspect = Assert.IsAssignableFrom<IAsyncRelayCommand>(inspector.Command);
            await WorkbenchRemoteControl.ClickAsync(window, "git.history.commit-details.toolbar");
            await Assert.IsAssignableFrom<Task>(inspect.ExecutionTask).WaitAsync(token);
            Assert.Equal("Merge history feature", (await WaitAsync<TextBlock>("git.history.commit-message")).Text);
            var changed = await WaitAsync<ItemsControl>("git.history.changed-files");
            Assert.Contains(Assert.IsAssignableFrom<IEnumerable<RetroIde.Application.Git.GitHistoryOperations.ChangedPath>>(changed.ItemsSource),
                path => string.Equals(path.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
            await connection.ChangeAsync("git_close_history", new(StringComparer.Ordinal)
            {
                ["viewKey"] = viaAgent.GetProperty("data").GetProperty("viewKey").GetGuid(),
            });
            Assert.NotEqual(tab, session.Layout.SelectedDocumentId);
        }
        finally { window.Close(); }

        async Task<T> WaitAsync<T>(string id) where T : Control
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                window.UpdateLayout();
                var match = window.GetVisualDescendants().OfType<T>().SingleOrDefault(item => item.IsEffectivelyVisible && HasId(item, id));
                if (match is not null)
                    return match;
                await Task.Delay(20, token);
            }
        }

        static bool HasId(Control item, string id) =>
            string.Equals(AutomationProperties.GetAutomationId(item), id, StringComparison.Ordinal);
    }
}
