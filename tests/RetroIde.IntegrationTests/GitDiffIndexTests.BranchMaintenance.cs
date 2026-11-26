using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Preserves tracking and rejects stale, unmerged, and checked-out branch maintenance.</summary>
    /// <remarks>FR-RETRO-013; TR-RETRO-GIT-013; TEST-RETRO-013.</remarks>
    [AvaloniaFact]
    public async Task Workbench_renames_and_safely_deletes_local_branches()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var checkout = sample.Primary;
        var original = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "branch", "--track", "topic/tracked", "origin/main");
        await sample.GitAsync(checkout, token, "switch", "-c", "topic/unmerged");
        GitWorktreeFixture.Write(checkout, GitWorktreeFixture.ConflictPath, "exclusive branch edit\n");
        await sample.GitAsync(checkout, token, "--literal-pathspecs", "add", "--", GitWorktreeFixture.ConflictPath);
        await sample.GitAsync(checkout, token, "commit", "-m", "Unmerged branch change");
        var unmerged = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "switch", "main");
        var sibling = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim();

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "branch-maintenance-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });

        Assert.Equal("git_branch_in_use", await ChangeAsync("git_rename_branch", "fixture-linked", sibling, "topic/sibling"));
        Assert.Equal("git_branch_in_use", await ChangeAsync("git_delete_branch", "fixture-linked", sibling));
        Assert.Equal("git_branch_in_use", await ChangeAsync("git_delete_branch", "main", original));
        Assert.Equal("git_branch_unmerged", await ChangeAsync("git_delete_branch", "topic/unmerged", unmerged));
        Assert.Equal("git_branch_changed", await ChangeAsync("git_rename_branch", "topic/tracked", unmerged, "topic/stale"));
        Assert.Equal("git_branch_exists", await ChangeAsync("git_rename_branch", "topic/tracked", original, "topic/unmerged"));
        Assert.Equal("git_branch_name_invalid", await ChangeAsync("git_rename_branch", "topic/tracked", original, "@{-1}"));

        Assert.Equal("ok", await ChangeAsync("git_rename_branch", "topic/tracked", original, "topic/renamed", stdio));
        Assert.Equal("origin/main", (await sample.GitAsync(checkout, token, "rev-parse", "--abbrev-ref", "topic/renamed@{upstream}")).Trim());
        var renamedLog = await sample.GitAsync(checkout, token, "reflog", "show", "--format=%gs", "refs/heads/topic/renamed");
        Assert.Contains("renamed", renamedLog, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0, (await sample.RunAsync(checkout, token, "show-ref", "--verify", "refs/heads/topic/tracked")).ExitCode);
        Assert.Equal("ok", await ChangeAsync("git_delete_branch", "topic/renamed", original, transport: stdio));
        Assert.NotEqual(0, (await sample.RunAsync(checkout, token, "show-ref", "--verify", "refs/heads/topic/renamed")).ExitCode);
        Assert.Equal(unmerged, (await sample.GitAsync(checkout, token, "rev-parse", "refs/heads/topic/unmerged")).Trim());

        await sample.GitAsync(checkout, token, "push", "origin", "topic/unmerged:refs/heads/reviewed-fixture");
        await sample.GitAsync(checkout, token, "branch", "--track", "topic/published", "origin/reviewed-fixture");
        Assert.Equal("ok", await ChangeAsync("git_delete_branch", "topic/published", unmerged));
        Assert.Equal(unmerged, (await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/reviewed-fixture")).Trim());

        Assert.Equal("ok", await ChangeAsync("git_rename_branch", "main", original, "topic/current"));
        Assert.Equal("topic/current", (await sample.GitAsync(checkout, token, "branch", "--show-current")).Trim());
        Assert.Equal("fixture-linked", (await sample.GitAsync(sample.Linked, token, "branch", "--show-current")).Trim());
        await VerifyBranchMaintenanceViewAsync(session, sample, token);

        async Task<string?> ChangeAsync(string tool, string branch, string expected, string? newName = null, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
            var args = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout, ["branchName"] = branch, ["expectedHead"] = expected,
                ["context"] = context.GetProperty("context"),
            };
            if (newName is not null)
                args["newName"] = newName;
            return (await connection.CallAsync(tool, args, transport).ConfigureAwait(false)).GetProperty("code").GetString();
        }
    }

    private static async Task VerifyBranchMaintenanceViewAsync(WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        await sample.GitAsync(sample.Primary, token, "branch", "--no-track", "topic/desktop-maintenance");
        var window = new WorkbenchWindow(session) { Width = 1360, Height = 900 };
        window.Show();
        try
        {
            await session.SourceControl.OpenHistoryCommand.ExecuteAsync(null).WaitAsync(token);
            Select("topic/desktop-maintenance");
            var rename = await OpenDialogAsync("git.branch.rename.toolbar");
            Find<TextBox>(rename.Dialog, "git.branch.name").Text = "topic/desktop-renamed";
            await WorkbenchRemoteControl.ClickAsync(rename.Dialog, "git.branch.submit");
            await rename.Completion.WaitAsync(token);
            Assert.Equal(0, (await sample.RunAsync(sample.Primary, token, "show-ref", "--verify", "refs/heads/topic/desktop-renamed")).ExitCode);

            Select("topic/desktop-renamed");
            var cancel = await OpenDialogAsync("git.branch.delete.toolbar");
            await WorkbenchRemoteControl.ClickAsync(cancel.Dialog, "git.branch.cancel");
            await cancel.Completion.WaitAsync(token);
            Assert.Equal(0, (await sample.RunAsync(sample.Primary, token, "show-ref", "--verify", "refs/heads/topic/desktop-renamed")).ExitCode);

            var delete = await OpenDialogAsync("git.branch.delete.toolbar");
            await WorkbenchRemoteControl.ClickAsync(delete.Dialog, "git.branch.submit");
            await delete.Completion.WaitAsync(token);
            Assert.NotEqual(0, (await sample.RunAsync(sample.Primary, token, "show-ref", "--verify", "refs/heads/topic/desktop-renamed")).ExitCode);
        }
        finally { window.Close(); }

        void Select(string name)
        {
            var list = Find<ListBox>(window, "git.history.refs");
            var refs = Assert.IsAssignableFrom<IEnumerable<GitHistoryOperations.Reference>>(list.ItemsSource);
            list.SelectedItem = Assert.Single(refs, reference =>
                string.Equals(reference.Name, "refs/heads/" + name, StringComparison.Ordinal));
        }

        async Task<(Window Dialog, Task Completion)> OpenDialogAsync(string id)
        {
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(Find<Button>(window, id).Command);
            await WorkbenchRemoteControl.ClickAsync(window, id);
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token);
            return (Assert.Single(window.OwnedWindows), Assert.IsAssignableFrom<Task>(command.ExecutionTask));
        }

        static T Find<T>(Window window, string id) where T : Control
        {
            window.UpdateLayout();
            return Assert.Single(window.GetVisualDescendants().OfType<T>(), control =>
                string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal));
        }
    }
}
