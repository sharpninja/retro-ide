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
    /// <summary>Verifies native remote configuration and fetch with a local bare repository and independent checkouts.</summary>
    /// <remarks>FR-RETRO-013; TR-RETRO-GIT-013; TEST-RETRO-013.</remarks>
    [AvaloniaFact]
    public async Task Workbench_configures_Git_remotes_and_fetches_divergence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(95));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var checkout = sample.Primary;
        await sample.GitAsync(checkout, token, "remote", "add", "redaction-only",
            "https://fixture:credential-marker@example.invalid/review?query-marker=value");
        await sample.GitAsync(checkout, token, "commit", "--allow-empty", "-m", "Local-only remote fixture commit");
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Remote-only fixture commit");
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main");
        var head = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim();
        var other = (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim();

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "remotes-stdio.json"));
        var root = await connection.AddRootAsync(checkout);
        await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" });

        var reviewed = await ListAsync();
        Assert.DoesNotContain("credential-marker", reviewed.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("query-marker", reviewed.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("https://example.invalid/review", reviewed.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("ok", await ConfigureAsync("git_add_remote", reviewed, "review", sample.Remote, stdio));
        Assert.Equal("git_remote_changed", await ConfigureAsync("git_rename_remote", reviewed, "review", "stale"));
        Assert.Equal("ok", await ConfigureAsync("git_rename_remote", await ListAsync(), "review", "mirror"));
        Assert.Equal("ok", await ConfigureAsync("git_set_remote_url", await ListAsync(), "mirror", sample.Remote, stdio));

        await sample.GitAsync(checkout, token, "config", "--add", "remote.mirror.fetch", "+refs/heads/main:refs/heads/forbidden");
        var refused = await MutateAsync("git_fetch", new(StringComparer.Ordinal)
        {
            ["name"] = "mirror", ["revision"] = (await ListAsync()).GetProperty("revision").GetString(),
        });
        Assert.Equal("git_fetch_refspec_unsupported", refused.GetProperty("code").GetString());
        Assert.NotEqual(0, (await sample.RunAsync(checkout, token, "show-ref", "--verify", "refs/heads/forbidden")).ExitCode);
        await sample.GitAsync(checkout, token, "config", "--unset-all", "remote.mirror.fetch");
        await sample.GitAsync(checkout, token, "config", "--add", "remote.mirror.fetch", "+refs/heads/*:refs/remotes/mirror/*");
        var changed = Path.Combine(checkout, GitWorktreeFixture.ChangedPath);
        var working = await File.ReadAllBytesAsync(changed, token);
        var fetched = await MutateAsync("git_fetch", new(StringComparer.Ordinal)
        {
            ["name"] = "mirror", ["revision"] = (await ListAsync()).GetProperty("revision").GetString(),
        }, stdio);
        Assert.Equal("ok", fetched.GetProperty("code").GetString());
        var remoteTip = (await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/main")).Trim();
        Assert.Equal(remoteTip, (await sample.GitAsync(checkout, token, "rev-parse", "refs/remotes/mirror/main")).Trim());

        var tracked = await MutateAsync("git_set_upstream", new(StringComparer.Ordinal)
        {
            ["branchName"] = "main", ["expectedHead"] = head, ["upstream"] = "refs/remotes/mirror/main",
        });
        Assert.Equal("ok", tracked.GetProperty("code").GetString());
        var status = (await connection.CallAsync("git_get_status")).GetProperty("data");
        Assert.Equal("mirror/main", status.GetProperty("upstream").GetString());
        Assert.Equal(1, status.GetProperty("ahead").GetInt32());
        Assert.Equal(1, status.GetProperty("behind").GetInt32());
        Assert.Equal(head, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD")).Trim());
        Assert.Equal(other, (await sample.GitAsync(sample.Linked, token, "rev-parse", "HEAD")).Trim());
        Assert.Equal(working, await File.ReadAllBytesAsync(changed, token));

        Assert.Equal("ok", await ConfigureAsync("git_remove_remote", await ListAsync(), "mirror"));
        Assert.DoesNotContain((await ListAsync()).GetProperty("remotes").EnumerateArray(), remote =>
            string.Equals("mirror", remote.GetProperty("name").GetString(), StringComparison.Ordinal));
        var restored = await MutateAsync("git_set_upstream", new(StringComparer.Ordinal)
        {
            ["branchName"] = "main", ["expectedHead"] = head, ["upstream"] = "refs/remotes/origin/main",
        });
        Assert.Equal("ok", restored.GetProperty("code").GetString());
        await VerifyRemoteControlsAsync(session, sample, token);

        async Task<JsonElement> ListAsync()
        {
            var output = await connection.CallAsync("git_list_remotes", new(StringComparer.Ordinal) { ["worktreePath"] = checkout });
            Assert.Equal("ok", output.GetProperty("code").GetString());
            return output.GetProperty("data");
        }

        async Task<string?> ConfigureAsync(string tool, JsonElement review, string name, string? value = null, McpClient? transport = null)
        {
            var args = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["revision"] = review.GetProperty("revision").GetString(), ["name"] = name,
            };
            if (value is not null)
                args["value"] = value;
            return (await MutateAsync(tool, args, transport)).GetProperty("code").GetString();
        }

        async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> args, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context");
            args["worktreePath"] = checkout;
            args["context"] = context.GetProperty("context");
            return await connection.CallAsync(tool, args, transport);
        }
    }

    private static async Task VerifyRemoteControlsAsync(WorkbenchSession session, GitWorktreeFixture sample, CancellationToken token)
    {
        var window = new WorkbenchWindow(session) { Width = 1360, Height = 900 };
        window.Show();
        try
        {
            await session.SourceControl.OpenHistoryCommand.ExecuteAsync(null).WaitAsync(token);
            var opening = await ClickAsync(window, "git.remotes.open.toolbar");
            var dialog = await WaitForDialogAsync();
            Find<TextBox>(dialog, "git.remotes.name").Text = "desktop";
            Find<TextBox>(dialog, "git.remotes.url").Text = sample.Remote;
            await (await ClickAsync(dialog, "git.remote.add.toolbar")).WaitAsync(token);
            var choices = Find<ListBox>(dialog, "git.remotes.list");
            var remotes = Assert.IsAssignableFrom<IEnumerable<GitRemoteOperations.Remote>>(choices.ItemsSource);
            Assert.Contains(remotes, remote => string.Equals("desktop", remote.Name, StringComparison.Ordinal));
            choices.SelectedItem = Assert.Single(remotes, remote => string.Equals("origin", remote.Name, StringComparison.Ordinal));
            await (await ClickAsync(dialog, "git.remote.fetch.toolbar")).WaitAsync(token);
            Assert.Equal("Ahead 1 · Behind 1", Find<TextBlock>(dialog, "git.remotes.divergence").Text);
            await WorkbenchRemoteControl.ClickAsync(dialog, "git.remotes.close");
            await opening.WaitAsync(token);

            var refs = Find<ListBox>(window, "git.history.refs");
            refs.SelectedItem = Assert.Single(Assert.IsAssignableFrom<IEnumerable<GitHistoryOperations.Reference>>(refs.ItemsSource),
                reference => string.Equals("refs/heads/main", reference.Name, StringComparison.Ordinal));
            var upstream = await ClickAsync(window, "git.branch.upstream.toolbar");
            dialog = await WaitForDialogAsync();
            await WorkbenchRemoteControl.ClickAsync(dialog, "git.upstream.clear");
            await upstream.WaitAsync(token);
            Assert.NotEqual(0, (await sample.RunAsync(sample.Primary, token, "rev-parse", "--abbrev-ref", "@{upstream}")).ExitCode);
        }
        finally { window.Close(); }

        async Task<Task> ClickAsync(Window owner, string id)
        {
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(Find<Button>(owner, id).Command);
            await WorkbenchRemoteControl.ClickAsync(owner, id);
            return Assert.IsAssignableFrom<Task>(command.ExecutionTask);
        }

        async Task<Window> WaitForDialogAsync()
        {
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(20, token);
            return Assert.Single(window.OwnedWindows);
        }

        static T Find<T>(Window owner, string id) where T : Control
        {
            owner.UpdateLayout();
            return Assert.Single(owner.GetVisualDescendants().OfType<T>(), control =>
                string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal));
        }
    }
}
