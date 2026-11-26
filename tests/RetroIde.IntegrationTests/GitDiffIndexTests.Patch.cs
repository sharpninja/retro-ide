using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Round-trips native Git patch bytes with independent index and worktree effects.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_reviews_and_applies_native_Git_patches()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var file = GitWorktreeFixture.ConflictPath;
        const string Binary = "patch café data.bin";
        var binary = new byte[] { 0, 0xfe, 0xff, 0xb1, 0x48, 0, 0x12 };
        GitWorktreeFixture.Write(sample.Primary, file, "staged patch line\n");
        await File.WriteAllBytesAsync(Path.Combine(sample.Primary, Binary), binary, token);
        await sample.GitAsync(sample.Primary, token, "add", "--", file, Binary);
        GitWorktreeFixture.Write(sample.Primary, file, "working patch line\n");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "patch-stdio.json"));
        var primaryRoot = await connection.AddRootAsync(sample.Primary);
        var linkedRoot = await connection.AddRootAsync(sample.Linked);
        await SelectAsync(primaryRoot);
        var index = await ExportAsync("index", stdio);
        var worktree = await ExportAsync("worktree");
        Assert.Contains("GIT binary patch", Decode(index), StringComparison.Ordinal);
        Assert.Contains("+staged patch line", Decode(index), StringComparison.Ordinal);
        Assert.Contains("+working patch line", Decode(worktree), StringComparison.Ordinal);
        Assert.DoesNotContain("GIT binary patch", Decode(worktree), StringComparison.Ordinal);

        await SelectAsync(linkedRoot);
        var indexed = await ReviewAsync(index, "index");
        Assert.Contains(Binary, indexed.GetProperty("paths").EnumerateArray().Select(path => path.GetString()));
        var unrelated = GitWorktreeFixture.HunkPath;
        GitWorktreeFixture.Write(sample.Linked, unrelated, "unrelated staged change\n");
        await sample.GitAsync(sample.Linked, token, "add", "--", unrelated);
        Assert.Equal("git_patch_changed", Code(await ApplyAsync(index, "index", indexed, stdio)));
        Assert.Equal(GitWorktreeFixture.SeedConflict, await sample.GitAsync(sample.Linked, token, "show", ":" + file));
        await sample.GitAsync(sample.Linked, token, "restore", "--source=HEAD", "--staged", "--worktree", "--", unrelated);
        Assert.Equal("ok", Code(await ApplyAsync(index, "index", await ReviewAsync(index, "index"))));
        Assert.Equal("staged patch line\n", await sample.GitAsync(sample.Linked, token, "show", ":" + file));
        Assert.Equal(GitWorktreeFixture.SeedConflict, await File.ReadAllTextAsync(Path.Combine(sample.Linked, file), token));
        Assert.False(File.Exists(Path.Combine(sample.Linked, Binary)));

        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
        {
            ["rootId"] = linkedRoot, ["relativePath"] = file,
        });
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var reviewed = await ReviewAsync(index, "worktree");
        await EditAsync("unrelated-to-disk editor draft");
        Assert.Equal("git_patch_editors_busy", Code(await ApplyAsync(index, "worktree", reviewed)));
        Assert.Equal("unrelated-to-disk editor draft", view.Buffer.Text);
        await EditAsync(GitWorktreeFixture.SeedConflict);
        Assert.Equal("ok", Code(await ApplyAsync(index, "worktree", reviewed, stdio)));
        Assert.Equal("staged patch line\n", view.Buffer.Text);
        Assert.Equal(binary, await File.ReadAllBytesAsync(Path.Combine(sample.Linked, Binary), token));
        var second = await ReviewAsync(worktree, "worktree");
        Assert.Equal("ok", Code(await ApplyAsync(worktree, "worktree", second)));
        Assert.Equal("working patch line\n", view.Buffer.Text);
        Assert.Equal("staged patch line\n", await sample.GitAsync(sample.Linked, token, "show", ":" + file));
        Assert.Equal("working patch line\n", await File.ReadAllTextAsync(Path.Combine(sample.Primary, file), token));

        const string Renamed = "renamed café.txt";
        await sample.GitAsync(sample.Primary, token, "mv", "--", unrelated, Renamed);
        var renameText = await sample.GitAsync(sample.Primary, token, "diff", "--cached", "--binary", "-M", "--", unrelated, Renamed);
        Assert.Contains("rename from ", renameText, StringComparison.Ordinal);
        var rename = Convert.ToBase64String(Encoding.UTF8.GetBytes(renameText));
        var renameReview = await ReviewAsync(rename, "worktree");
        Assert.Equal(new[] { unrelated, Renamed }.Order(StringComparer.Ordinal),
            renameReview.GetProperty("paths").EnumerateArray().Select(path => path.GetString()), StringComparer.Ordinal);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
        {
            ["rootId"] = linkedRoot, ["relativePath"] = unrelated,
        });
        var renamedView = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var original = renamedView.Buffer.Text;
        await EditAsync("draft at rename source", renamedView);
        Assert.Equal("git_patch_editors_busy", Code(await ApplyAsync(rename, "worktree", renameReview)));
        Assert.False(File.Exists(Path.Combine(sample.Linked, Renamed)));
        await EditAsync(original, renamedView);
        Assert.Equal("ok", Code(await ApplyAsync(rename, "worktree", renameReview)));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(sample.Linked, Renamed), token));
        Assert.False(File.Exists(Path.Combine(sample.Linked, unrelated)));

        async Task<string> ExportAsync(string layer, McpClient? transport = null)
        {
            var result = await connection.CallAsync("git_export_patch", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["layer"] = layer,
            }, transport).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return Assert.IsType<string>(result.GetProperty("data").GetProperty("patchBase64").GetString());
        }
        async Task<JsonElement> ReviewAsync(string patch, string layer)
        {
            var result = await connection.CallAsync("git_review_patch", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Linked, ["patchBase64"] = patch, ["layer"] = layer,
            }).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data");
        }
        async Task<JsonElement> ApplyAsync(string patch, string layer, JsonElement review, McpClient? transport = null) =>
            await connection.CallAsync("git_apply_patch", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Linked, ["patchBase64"] = patch, ["layer"] = layer,
                ["revision"] = review.GetProperty("revision").GetString(),
                ["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context"),
            }, transport).ConfigureAwait(false);
        async Task SelectAsync(Guid rootId) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = rootId, ["relativePath"] = "",
        }).ConfigureAwait(false);
        async Task EditAsync(string text, DocumentViewModel? target = null)
        {
            target ??= view;
            await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = target.Capture(), ["offset"] = 0, ["length"] = target.Buffer.TextLength, ["text"] = text,
            }).ConfigureAwait(false);
        }
        static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
        static string? Code(JsonElement response) => response.GetProperty("code").GetString();
    }
}
