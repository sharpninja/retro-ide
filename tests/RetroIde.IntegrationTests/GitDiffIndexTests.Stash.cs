using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Exercises a reviewed native stash stack, stage restoration, and safe conflict handling across checkouts.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_manages_the_reviewed_Git_stash_stack()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var primary = sample.Primary;
        var file = GitWorktreeFixture.ConflictPath;
        const string Extra = "temporary café file.txt";
        await sample.GitAsync(primary, token, "config", "--local", "user.name", "Retro Stash Test");
        await sample.GitAsync(primary, token, "config", "--local", "user.email", "stash@example.invalid");
        await sample.GitAsync(primary, token, "config", "--local", "commit.gpgsign", "false");
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(primary, ".git", "stash-stdio.json"));
        var primaryRoot = await connection.AddRootAsync(primary);
        var linkedRoot = await connection.AddRootAsync(sample.Linked);
        await SelectAsync(primaryRoot);
        Assert.Empty((await ListAsync(primary)).GetProperty("entries").EnumerateArray());
        Assert.Equal("git_stash_no_changes", Code(await CreateAsync("empty", includeUntracked: false)));

        GitWorktreeFixture.Write(primary, file, "staged revision\n");
        await sample.GitAsync(primary, token, "add", "--", file);
        GitWorktreeFixture.Write(primary, file, "working revision\n");
        GitWorktreeFixture.Write(primary, Extra, "untracked content\n");
        Assert.Equal("ok", Code(await CreateAsync("Keep café changes", includeUntracked: true)));
        Assert.Equal(GitWorktreeFixture.SeedConflict, await File.ReadAllTextAsync(Path.Combine(primary, file), token));
        Assert.False(File.Exists(Path.Combine(primary, Extra)));
        var original = await ListAsync(primary);
        var entry = original.GetProperty("entries").EnumerateArray().Single();
        Assert.Contains("Keep café changes", entry.GetProperty("message").GetString(), StringComparison.Ordinal);
        var inspected = await InvokeEntryAsync("git_inspect_stash", primary, original, entry, mutation: false);
        Assert.Equal("ok", Code(inspected));
        Assert.Contains("working revision", inspected.GetProperty("data").GetProperty("patch").GetString(), StringComparison.Ordinal);

        await SelectAsync(linkedRoot);
        var linked = await ListAsync(sample.Linked);
        var linkedEntry = linked.GetProperty("entries").EnumerateArray().Single();
        Assert.Equal(entry.GetProperty("objectId").GetString(), linkedEntry.GetProperty("objectId").GetString());
        Assert.Equal("git_worktree_mismatch", Code(await InvokeEntryAsync("git_apply_stash", primary, original, entry, mutation: true)));
        Assert.Equal("ok", Code(await InvokeEntryAsync("git_apply_stash", sample.Linked, linked, linkedEntry,
            mutation: true, restoreIndex: true, transport: stdio)));
        Assert.Equal("staged revision\n", await sample.GitAsync(sample.Linked, token, "show", ":" + file));
        Assert.Equal("working revision\n", await File.ReadAllTextAsync(Path.Combine(sample.Linked, file), token));
        Assert.Equal("untracked content\n", await File.ReadAllTextAsync(Path.Combine(sample.Linked, Extra), token));
        Assert.Equal(GitWorktreeFixture.SeedConflict, await File.ReadAllTextAsync(Path.Combine(primary, file), token));
        Assert.Single((await ListAsync(sample.Linked)).GetProperty("entries").EnumerateArray());

        await SelectAsync(primaryRoot);
        GitWorktreeFixture.Write(primary, GitWorktreeFixture.HunkPath, "external stash content\n");
        await sample.GitAsync(primary, token, "stash", "push", "-m", "External stash");
        Assert.Equal("git_stash_changed", Code(await InvokeEntryAsync("git_drop_stash", primary, original, entry, mutation: true)));
        var fresh = await ListAsync(primary);
        Assert.Equal("ok", Code(await InvokeEntryAsync("git_drop_stash", primary, fresh,
            fresh.GetProperty("entries").EnumerateArray().First(), mutation: true, transport: stdio)));

        GitWorktreeFixture.Write(primary, file, "conflicting committed change\n");
        await sample.GitAsync(primary, token, "add", "--", file);
        await sample.GitAsync(primary, token, "commit", "-m", "Conflicting commit");
        fresh = await ListAsync(primary);
        var pop = await InvokeEntryAsync("git_pop_stash", primary, fresh, fresh.GetProperty("entries").EnumerateArray().Single(), mutation: true);
        Assert.Equal("git_stash_conflict", Code(pop));
        Assert.Equal(file, pop.GetProperty("data").GetProperty("conflicts").EnumerateArray().Single().GetString());
        Assert.Single((await ListAsync(primary)).GetProperty("entries").EnumerateArray());
        await sample.GitAsync(primary, token, "reset", "--hard", "HEAD");
        File.Delete(Path.Combine(primary, Extra));
        await sample.GitAsync(primary, token, "reset", "--hard", "HEAD^");
        fresh = await ListAsync(primary);
        Assert.Equal("ok", Code(await InvokeEntryAsync("git_pop_stash", primary, fresh,
            fresh.GetProperty("entries").EnumerateArray().Single(), mutation: true, restoreIndex: true, transport: stdio)));
        Assert.Equal("staged revision\n", await sample.GitAsync(primary, token, "show", ":" + file));
        Assert.Equal("working revision\n", await File.ReadAllTextAsync(Path.Combine(primary, file), token));
        Assert.Empty((await ListAsync(primary)).GetProperty("entries").EnumerateArray());

        async Task SelectAsync(Guid id) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = id, ["relativePath"] = "",
        }).ConfigureAwait(false);
        async Task<JsonElement> CreateAsync(string message, bool includeUntracked) =>
            await CallAsync("git_create_stash", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["message"] = message, ["includeUntracked"] = includeUntracked,
            }, mutation: true).ConfigureAwait(false);
        async Task<JsonElement> ListAsync(string checkout)
        {
            var response = await connection.CallAsync("git_list_stashes", new(StringComparer.Ordinal) { ["worktreePath"] = checkout }).ConfigureAwait(false);
            Assert.Equal("ok", Code(response));
            return response.GetProperty("data");
        }
        async Task<JsonElement> InvokeEntryAsync(string name, string checkout, JsonElement stack, JsonElement item, bool mutation,
            bool restoreIndex = false, McpClient? transport = null) =>
            await CallAsync(name, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = checkout, ["revision"] = stack.GetProperty("revision").GetString(),
                ["selector"] = item.GetProperty("selector").GetString(), ["objectId"] = item.GetProperty("objectId").GetString(),
                ["restoreIndex"] = restoreIndex,
            }, mutation, transport).ConfigureAwait(false);
        async Task<JsonElement> CallAsync(string name, Dictionary<string, object?> args, bool mutation, McpClient? transport = null)
        {
            if (mutation)
                args["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            return await connection.CallAsync(name, args, transport).ConfigureAwait(false);
        }
        static string? Code(JsonElement response) => response.GetProperty("code").GetString();
    }
}
