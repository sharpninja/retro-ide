using System.Text.Json;
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
    /// <summary>Creates and switches real local branches without overwriting dirty files or crossing checkout scope.</summary>
    /// <remarks>TEST-RETRO-013; TR-RETRO-GIT-013; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Workbench_creates_and_switches_Git_branches_without_overwriting_changes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var path = GitWorktreeFixture.ChangedPath;
        var checkout = sample.Primary;
        var linked = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim();
        var baseline = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "switch", "-c", "feature/review");
        GitWorktreeFixture.Write(checkout, path, "feature text\n");
        const string Ignored = "local.ignored";
        GitWorktreeFixture.Write(checkout, Ignored, "tracked on feature\n");
        await sample.GitAsync(checkout, token, "--literal-pathspecs", "add", "--", path, Ignored);
        await sample.GitAsync(checkout, token, "commit", "-m", "Branch-specific source");
        var feature = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "switch", "main");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "branch-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });

        var created = await MutateAsync("git_create_branch", new(StringComparer.Ordinal)
        {
            ["branchName"] = "topic/from-review", ["startCommit"] = feature,
        }, stdio);
        Assert.Equal("ok", created.GetProperty("code").GetString());
        Assert.Equal("main", (await sample.GitAsync(checkout, token, "branch", "--show-current")).Trim());
        Assert.Equal(feature, (await sample.GitAsync(checkout, token, "rev-parse", "refs/heads/topic/from-review")).Trim());
        var duplicate = await MutateAsync("git_create_branch", new(StringComparer.Ordinal)
        {
            ["branchName"] = "topic/from-review", ["startCommit"] = baseline,
        }, stdio);
        Assert.Equal("git_branch_exists", duplicate.GetProperty("code").GetString());
        var invalid = await MutateAsync("git_create_branch", new(StringComparer.Ordinal)
        {
            ["branchName"] = "@{-1}", ["startCommit"] = baseline,
        });
        Assert.Equal("git_branch_name_invalid", invalid.GetProperty("code").GetString());

        Assert.Equal("git_branch_in_use", await SwitchAsync("fixture-linked", linked));
        Assert.Equal("git_branch_changed", await SwitchAsync("topic/from-review", baseline));
        GitWorktreeFixture.Write(checkout, path, "dirty working file\n");
        Assert.Equal("git_branch_switch_failed", await SwitchAsync("topic/from-review", feature));
        Assert.Equal("dirty working file\n", await File.ReadAllTextAsync(Path.Combine(checkout, path), token));
        Assert.Equal("main", (await sample.GitAsync(checkout, token, "branch", "--show-current")).Trim());

        await sample.GitAsync(checkout, token, "--literal-pathspecs", "restore", "--", path);
        await File.AppendAllTextAsync(Path.Combine(checkout, ".git", "info", "exclude"), "\n" + Ignored + "\n", token);
        GitWorktreeFixture.Write(checkout, Ignored, "keep the ignored file\n");
        Assert.Equal("git_branch_switch_failed", await SwitchAsync("topic/from-review", feature));
        Assert.Equal("keep the ignored file\n", await File.ReadAllTextAsync(Path.Combine(checkout, Ignored), token));
        File.Delete(Path.Combine(checkout, Ignored));

        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = path });
        var editor = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var baselineBuffer = editor.Buffer.Text;
        await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = editor.Capture(), ["offset"] = 0, ["length"] = editor.Buffer.TextLength, ["text"] = "unsaved editor text",
        });
        Assert.Equal("git_branch_editors_busy", await SwitchAsync("topic/from-review", feature));
        Assert.Equal("unsaved editor text", editor.Buffer.Text);
        await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = editor.Capture(), ["offset"] = 0, ["length"] = editor.Buffer.TextLength, ["text"] = baselineBuffer,
        });
        Assert.False(editor.IsDirty);
        Assert.Equal("ok", await SwitchAsync("topic/from-review", feature));
        Assert.Equal("feature text\n", await File.ReadAllTextAsync(Path.Combine(checkout, path), token));
        Assert.Equal("topic/from-review", (await sample.GitAsync(checkout, token, "branch", "--show-current")).Trim());
        Assert.Same(editor, session.Layout.CurrentSource);
        Assert.Equal("feature text\n", editor.Buffer.Text);
        Assert.False(editor.IsDirty);
        Assert.Equal(linked, (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim());

        var hooks = Directory.CreateDirectory(Path.Combine(checkout, ".git", "branch-test-hooks"));
        await sample.GitAsync(checkout, token, "config", "--local", "core.hooksPath", hooks.FullName);
        var afterCheckout = Path.Combine(hooks.FullName, "post-checkout");
        await File.WriteAllTextAsync(afterCheckout, "#!/bin/sh\nexit 1\n", token);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(afterCheckout, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var switched = await MutateAsync("git_switch_branch", new(StringComparer.Ordinal)
        {
            ["branchName"] = "main", ["expectedHead"] = baseline,
        });
        Assert.Equal("ok", switched.GetProperty("code").GetString());
        Assert.Contains("post-checkout hook", switched.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(baselineBuffer, editor.Buffer.Text);
        File.Delete(afterCheckout);
        await VerifyBranchViewAsync(session, sample, token);

        async Task<string?> SwitchAsync(string name, string expected) => (await MutateAsync("git_switch_branch", new(StringComparer.Ordinal)
        {
            ["branchName"] = name, ["expectedHead"] = expected,
        })).GetProperty("code").GetString();

        async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> args, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context");
            args["worktreePath"] = checkout;
            args["context"] = context.GetProperty("context");
            return await connection.CallAsync(tool, args, transport).ConfigureAwait(false);
        }
    }

    private static async Task VerifyBranchViewAsync(WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var window = new WorkbenchWindow(session) { Width = 1360, Height = 900 };
        window.Show();
        try
        {
            await session.SourceControl.OpenHistoryCommand.ExecuteAsync(null).WaitAsync(token);
            var commands = Assert.IsAssignableFrom<IAsyncRelayCommand>(Find<Button>(window, "git.branch.create.toolbar").Command);
            await WorkbenchRemoteControl.ClickAsync(window, "git.branch.create.toolbar");
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token);
            var dialog = Assert.Single(window.OwnedWindows);
            Find<TextBox>(dialog, "git.branch.name").Text = "topic/desktop";
            Find<CheckBox>(dialog, "git.branch.checkout").IsChecked = true;
            await WorkbenchRemoteControl.ClickAsync(dialog, "git.branch.submit");
            await Assert.IsAssignableFrom<Task>(commands.ExecutionTask).WaitAsync(token);
            Assert.Equal("topic/desktop", (await sample.GitAsync(sample.Primary, token, "branch", "--show-current")).Trim());

            var refs = Find<ListBox>(window, "git.history.refs");
            var local = Assert.IsAssignableFrom<IEnumerable<GitHistoryOperations.Reference>>(refs.ItemsSource);
            refs.SelectedItem = Assert.Single(local, value => string.Equals(value.Name, "refs/heads/main", StringComparison.Ordinal));
            var change = Assert.IsAssignableFrom<IAsyncRelayCommand>(Find<Button>(window, "git.branch.switch.toolbar").Command);
            await WorkbenchRemoteControl.ClickAsync(window, "git.branch.switch.toolbar");
            await Assert.IsAssignableFrom<Task>(change.ExecutionTask).WaitAsync(token);
            Assert.Equal("main", (await sample.GitAsync(sample.Primary, token, "branch", "--show-current")).Trim());
        }
        finally { window.Close(); }

        static T Find<T>(Window window, string id) where T : Control
        {
            window.UpdateLayout();
            return Assert.Single(window.GetVisualDescendants().OfType<T>(), control =>
                string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal));
        }
    }
}
