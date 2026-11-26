using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Proves Source Control opens both patch layers in real tabs and dispatches index commands.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-stage-and-push; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Source_Control_opens_diff_tabs_and_stages_selected_files()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        using var sample = await CreateDiffSampleAsync(token);
        await using var session = new WorkbenchSession(
            new WorkspaceExplorerStore(Path.Combine(sample.Primary, ".git", "retro-workspace.json")), null);
        var window = new WorkbenchWindow(session) { Width = 1280, Height = 900 };
        window.Show();
        try
        {
            await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
            await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "retro-stdio.json"));
            var root = await connection.AddRootAsync(sample.Primary);
            await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
            {
                ["rootId"] = root, ["relativePath"] = "",
            });
            await connection.ChangeAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "source-control" });
            await connection.CallAsync("git_get_status");

            Select("git.status.working");
            await ClickAsync("git.diff.open.pane");
            var workingTab = Assert.IsType<string>(session.Layout.SelectedDocumentId);
            var editor = Find<TextEditor>("git.diff.patch");
            Assert.True(editor.IsReadOnly);
            Assert.Contains("+" + WorkingMark, editor.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(StagedMark, editor.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(workingTab, System.Text.Json.JsonSerializer.Serialize(session.Layout.Capture()), StringComparison.Ordinal);

            var staged = await connection.ChangeAsync("git_open_diff", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["path"] = GitWorktreeFixture.HunkPath, ["layer"] = "staged",
            }, stdio);
            var stagedTab = Assert.IsType<string>(session.Layout.SelectedDocumentId);
            Assert.NotEqual(workingTab, stagedTab);
            var stagedEditor = await ReadPatchAsync(StagedMark);
            Assert.DoesNotContain(WorkingMark, stagedEditor.Text, StringComparison.Ordinal);
            var reopened = await connection.ChangeAsync("git_open_diff", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["path"] = GitWorktreeFixture.HunkPath, ["layer"] = "staged",
            });
            Assert.Equal(staged.GetProperty("data").GetProperty("viewKey").GetGuid(), reopened.GetProperty("data").GetProperty("viewKey").GetGuid());
            await connection.ChangeAsync("git_close_diff", new(StringComparer.Ordinal)
            {
                ["viewKey"] = staged.GetProperty("data").GetProperty("viewKey").GetGuid(),
            });
            Assert.NotEqual(stagedTab, session.Layout.SelectedDocumentId);

            Select("git.status.working");
            await ClickAsync("git.file.stage.pane");
            await connection.CallAsync("git_get_status");
            var mcp = new McpPort(connection, sample.Primary);
            Assert.Contains("+" + WorkingMark, await mcp.DiffAsync(GitWorktreeFixture.HunkPath, true), StringComparison.Ordinal);
            Select("git.status.staged");
            await ClickAsync("git.file.unstage.pane");
            Assert.Empty(await mcp.DiffAsync(GitWorktreeFixture.HunkPath, true));
            Assert.Contains("+" + WorkingMark, await mcp.DiffAsync(GitWorktreeFixture.HunkPath, false), StringComparison.Ordinal);

            await connection.ChangeAsync("git_open_diff", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["path"] = GitWorktreeFixture.HunkPath, ["layer"] = "working",
            });
            await ReadPatchAsync(StagedMark);
            var hunks = Find<ListBox>("git.diff.hunks");
            hunks.SelectedItem = Assert.IsAssignableFrom<IEnumerable<GitHunkPatch.Hunk>>(hunks.ItemsSource).Last();
            await ClickAsync("git.hunks.stage.toolbar");
            var selectedPatch = await mcp.DiffAsync(GitWorktreeFixture.HunkPath, true);
            Assert.Contains("+" + WorkingMark, selectedPatch, StringComparison.Ordinal);
            Assert.DoesNotContain(StagedMark, selectedPatch, StringComparison.Ordinal);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(token);
        }

        void Select(string id)
        {
            var list = Find<ListBox>(id);
            list.SelectedItem = Assert.Single(Assert.IsAssignableFrom<IEnumerable<GitStatusEntry>>(list.ItemsSource),
                entry => string.Equals(entry.Path, GitWorktreeFixture.HunkPath, StringComparison.Ordinal));
        }

        T Find<T>(string id) where T : Control
        {
            window.UpdateLayout();
            var found = window.GetVisualDescendants().OfType<T>().SingleOrDefault(control =>
                string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal));
            Assert.True(found is not null, $"The workbench does not render {id}.");
            return found;
        }

        async Task ClickAsync(string id)
        {
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(Find<Button>(id).Command);
            var previous = command.ExecutionTask;
            await WorkbenchRemoteControl.ClickAsync(window, id);
            var execution = Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.NotSame(previous, execution);
            await execution.WaitAsync(token);
        }

        async Task<TextEditor> ReadPatchAsync(string addedLine)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var editor = Find<TextEditor>("git.diff.patch");
                if (editor.Text.Contains("+" + addedLine, StringComparison.Ordinal))
                    return editor;
                await Task.Delay(20, token);
            }
            var current = Find<TextEditor>("git.diff.patch");
            Assert.Contains("+" + addedLine, current.Text, StringComparison.Ordinal);
            return current;
        }
    }
}
