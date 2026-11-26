using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Uses native commit and amend and verifies that hook rejection preserves HEAD and the index.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
    [AvaloniaFact]
    public async Task Workbench_commit_and_amend_preserve_worktree_and_honor_hooks()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        using var sample = await CreateDiffSampleAsync(token);
        var checkout = sample.Primary;
        var path = GitWorktreeFixture.HunkPath;
        var originalHead = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        var linkedHead = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim();
        await sample.GitAsync(checkout, token, "config", "--local", "user.name", "Retro Workbench Test");
        await sample.GitAsync(checkout, token, "config", "--local", "user.email", "workbench@example.invalid");
        await sample.GitAsync(checkout, token, "config", "--local", "commit.gpgsign", "false");
        var hooks = Directory.CreateDirectory(Path.Combine(checkout, ".git", "workbench test hooks"));
        await sample.GitAsync(checkout, token, "config", "--local", "core.hooksPath", hooks.FullName);
        var hook = Path.Combine(hooks.FullName, "pre-commit");
        var index = Path.Combine(checkout, ".git", "index");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "commit-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        var linked = await connection.AddRootAsync(sample.Linked);
        var nested = await connection.AddRootAsync(Path.Combine(checkout, "src"));
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = nested, ["relativePath"] = "" });
        var scoped = await connection.CallAsync("git_get_commit_state", new(StringComparer.Ordinal) { ["worktreePath"] = checkout });
        Assert.Equal("git_commit_scope_required", scoped.GetProperty("code").GetString());
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });

        var state = await GetCommitStateAsync();
        await InstallHookAsync("#!/bin/sh\nprintf 'fixture hook rejected the commit\\n' >&2\nexit 1\n");
        var before = await File.ReadAllBytesAsync(index, token);
        var rejected = await CommitAsync("git_commit", state, "Rejected checkpoint");
        Assert.Equal("git_commit_failed", rejected.GetProperty("code").GetString());
        Assert.Contains("fixture hook rejected the commit", rejected.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(originalHead, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim());
        Assert.Equal(before, await File.ReadAllBytesAsync(index, token));

        await InstallHookAsync("#!/bin/sh\nexit 0\n");
        var signer = Path.Combine(hooks.FullName, "test-signer");
        await File.WriteAllTextAsync(signer, "#!/bin/sh\nprintf 'fixture signer refused to sign\\n' >&2\nexit 1\n", token);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(signer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await sample.GitAsync(checkout, token, "config", "--local", "gpg.format", "openpgp");
        await sample.GitAsync(checkout, token, "config", "--local", "gpg.program", signer);
        await sample.GitAsync(checkout, token, "config", "--local", "commit.gpgsign", "true");
        var unsigned = await CommitAsync("git_commit", state, "Signing rejection");
        Assert.Equal("git_commit_failed", unsigned.GetProperty("code").GetString());
        Assert.Contains("fixture signer refused to sign", unsigned.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(originalHead, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim());
        Assert.Equal(before, await File.ReadAllBytesAsync(index, token));
        await sample.GitAsync(checkout, token, "config", "--local", "commit.gpgsign", "false");

        await InstallHookAsync("#!/bin/sh\ntouch .git/fixture-commit-entered\nwhile [ ! -f .git/fixture-commit-release ]; do sleep 0.1; done\n");
        var pending = CommitAsync("git_commit", state, "Commit isolated staged changes");
        var entered = Path.Combine(checkout, ".git", "fixture-commit-entered");
        try
        {
            while (!File.Exists(entered))
            {
                Assert.False(pending.IsCompleted, "The commit exited before the fixture hook reached its barrier.");
                await Task.Delay(25, token);
            }
            await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = linked, ["relativePath"] = "" });
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(checkout, ".git", "fixture-commit-release"), "continue", CancellationToken.None);
        }
        var committed = await pending;
        await InstallHookAsync("#!/bin/sh\nexit 0\n");
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });
        Assert.Equal("ok", committed.GetProperty("code").GetString());
        var firstHead = committed.GetProperty("data").GetProperty("head").GetString();
        Assert.Equal(firstHead, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim());
        Assert.Equal(originalHead, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD^")).Trim());
        var firstPatch = await sample.GitAsync(checkout, token, "show", "HEAD:" + path);
        Assert.Contains(StagedMark, firstPatch, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkingMark, firstPatch, StringComparison.Ordinal);
        Assert.Contains(WorkingMark, await File.ReadAllTextAsync(Path.Combine(checkout, path), token), StringComparison.Ordinal);

        Assert.Equal("git_commit_state_changed", (await CommitAsync("git_amend", state, "Stale amend")).GetProperty("code").GetString());
        await sample.GitAsync(checkout, token, "add", "--", path);
        var amended = await CommitAsync("git_amend", await GetCommitStateAsync(), "Amend staged changes");
        Assert.Equal("ok", amended.GetProperty("code").GetString());
        Assert.NotEqual(firstHead, amended.GetProperty("data").GetProperty("head").GetString());
        Assert.Equal(originalHead, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD^")).Trim());
        Assert.Contains(WorkingMark, await sample.GitAsync(checkout, token, "show", "HEAD:" + path), StringComparison.Ordinal);
        Assert.Equal("Amend staged changes", (await sample.GitAsync(checkout, token, "log", "-1", "--format=%s")).Trim());
        Assert.Equal(linkedHead, (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim());
        Assert.Empty(await sample.GitAsync(sample.Linked, token, "diff", "--cached", "--", path));
        await VerifyAmendDialogAsync(session, sample, token);

        async Task<JsonElement> GetCommitStateAsync()
        {
            var result = await connection.CallAsync("git_get_commit_state", new(StringComparer.Ordinal) { ["worktreePath"] = checkout });
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result.GetProperty("data");
        }

        async Task<JsonElement> CommitAsync(string tool, JsonElement snapshot, string message)
        {
            var context = await connection.CallAsync("workbench_get_context");
            return await connection.CallAsync(tool, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout, ["preimage"] = snapshot.GetProperty("preimage").GetString(), ["message"] = message,
                ["context"] = context.GetProperty("context"),
            }, stdio);
        }

        async Task InstallHookAsync(string script)
        {
            await File.WriteAllTextAsync(hook, script, token);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static async Task VerifyAmendDialogAsync(WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            window.UpdateLayout();
            var menu = Assert.Single(window.GetVisualDescendants().OfType<MenuItem>(), item => HasId(item, "menu.git"));
            var amend = Assert.Single(Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource), item => HasId(item, "git.commit.amend.menu"));
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(amend.Command);
            Assert.NotNull(amend.Icon);
            Assert.NotNull(amend.InputGesture);
            var previousHead = await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD");

            var cancel = command.ExecuteAsync(null);
            var dialog = await WaitForDialogAsync();
            await WorkbenchRemoteControl.ClickAsync(dialog, "git.commit.cancel");
            await cancel.WaitAsync(token);
            Assert.Equal(previousHead, await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD"));

            var submit = command.ExecuteAsync(null);
            dialog = await WaitForDialogAsync();
            var message = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), input => HasId(input, "git.commit.message"));
            Assert.Equal("Amend staged changes", message.Text);
            message.Text = "Amend from the workbench";
            await WorkbenchRemoteControl.ClickAsync(dialog, "git.commit.submit");
            await submit.WaitAsync(token);
            Assert.Equal("ok", session.SourceControl.LastResult?.Code);
            Assert.Equal("Amend from the workbench", (await sample.GitAsync(sample.Primary, token, "log", "-1", "--format=%s")).Trim());
        }
        finally { window.Close(); }

        async Task<Window> WaitForDialogAsync()
        {
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token).ConfigureAwait(true);
            var dialog = Assert.Single(window.OwnedWindows);
            dialog.UpdateLayout();
            return dialog;
        }

        static bool HasId(Control control, string id) =>
            string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal);
    }
}
