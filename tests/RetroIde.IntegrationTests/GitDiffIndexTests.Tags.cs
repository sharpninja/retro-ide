using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Proves tag object and commit identity, shared refs, and guarded deletion with both MCP transports.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Git_tags_preserve_annotation_and_guard_against_external_ref_changes()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(95));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "user.name", "Retro IDE Fixture").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "user.email", "fixture@example.invalid").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "tag.gpgSign", "false").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "commit", "--allow-empty", "-m", "Release target").ConfigureAwait(true);
        var head = (await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        var earlier = (await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD^").ConfigureAwait(true)).Trim();
        const string Release = "release/café-v1";
        const string Check = "checkpoint";
        const string Annotation = "Release café\n\nTested both checkouts.";

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "tags-stdio.json")).ConfigureAwait(true);
        var primary = await connection.AddRootAsync(sample.Primary).ConfigureAwait(true);
        var linked = await connection.AddRootAsync(sample.Linked).ConfigureAwait(true);
        await SelectAsync(primary).ConfigureAwait(true);

        var create = Tag(sample.Primary, Release);
        create["targetCommit"] = head;
        create["message"] = Annotation;
        var made = (await connection.ChangeAsync("git_create_tag", create, stdio).ConfigureAwait(true)).GetProperty("data");
        var annotatedId = made.GetProperty("objectId").GetString();
        Assert.NotEqual(head, annotatedId);
        Assert.Equal("tag", made.GetProperty("objectType").GetString());
        Assert.Equal(head, made.GetProperty("targetObjectId").GetString());
        Assert.Equal("git_tag_exists", Code(await MutateAsync("git_create_tag", create).ConfigureAwait(true)));

        var lightweight = Tag(sample.Primary, Check);
        lightweight["targetCommit"] = earlier;
        var marker = (await connection.ChangeAsync("git_create_tag", lightweight).ConfigureAwait(true)).GetProperty("data");
        Assert.Equal(earlier, marker.GetProperty("objectId").GetString());
        Assert.Equal("commit", marker.GetProperty("objectType").GetString());

        Assert.Equal("git_worktree_mismatch", Code(await connection.CallAsync("git_get_tag", Tag(sample.Linked, Release)).ConfigureAwait(true)));
        await SelectAsync(linked).ConfigureAwait(true);
        var read = (await connection.CallAsync("git_get_tag", Tag(sample.Linked, Release), stdio).ConfigureAwait(true)).GetProperty("data");
        Assert.Equal(annotatedId, read.GetProperty("objectId").GetString());
        Assert.Equal("Release café", read.GetProperty("subject").GetString());
        Assert.Equal("Tested both checkouts.\n", read.GetProperty("body").GetString());
        Assert.False(string.IsNullOrWhiteSpace(read.GetProperty("taggerDate").GetString()));

        var remove = Tag(sample.Linked, Release);
        remove["expectedObjectId"] = annotatedId;
        await sample.GitAsync(sample.Primary, token, "-c", "tag.gpgSign=false", "tag", "--force", "--", Release, earlier).ConfigureAwait(true);
        Assert.Equal("git_tag_changed", Code(await MutateAsync("git_delete_tag", remove, stdio).ConfigureAwait(true)));
        Assert.Equal(earlier, (await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/tags/" + Release).ConfigureAwait(true)).Trim());
        remove["expectedObjectId"] = earlier;
        await connection.ChangeAsync("git_delete_tag", remove).ConfigureAwait(true);
        Assert.Equal("git_tag_unavailable", Code(await connection.CallAsync("git_get_tag", Tag(sample.Linked, Release)).ConfigureAwait(true)));
        Assert.Equal(earlier, (await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/tags/" + Check).ConfigureAwait(true)).Trim());
        Assert.Equal(head, (await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());

        await sample.GitAsync(sample.Primary, token, "config", "--local", "gpg.format", "openpgp").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "gpg.program", Path.Combine(sample.Primary, "absent-signer")).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "tag.gpgSign", "true").ConfigureAwait(true);
        var signed = Tag(sample.Linked, "required-signature");
        signed["targetCommit"] = head;
        signed["message"] = "Signing is required by this fixture.";
        Assert.Equal("git_tag_create_failed", Code(await MutateAsync("git_create_tag", signed).ConfigureAwait(true)));
        Assert.Equal("git_tag_unavailable", Code(await connection.CallAsync("git_get_tag", Tag(sample.Linked, "required-signature")).ConfigureAwait(true)));

        async Task SelectAsync(Guid root) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        }).ConfigureAwait(false);
        async Task<JsonElement> MutateAsync(string name, Dictionary<string, object?> args, McpClient? client = null)
        {
            args["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            return await connection.CallAsync(name, args, client).ConfigureAwait(false);
        }
        static Dictionary<string, object?> Tag(string worktree, string name) => new(StringComparer.Ordinal)
        {
            ["worktreePath"] = worktree, ["tagName"] = name,
        };
        static string? Code(JsonElement result) => result.GetProperty("code").GetString();
    }
}
