using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Exercises configured credentials and SSH signing against Git's real smart HTTP backend.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-native-host.</remarks>
    [AvaloniaFact]
    public async Task Workbench_authenticates_fetch_pull_and_push_and_preserves_the_signed_commit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        var checkout = sample.Primary;
        var baseTip = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Remote authentication fixture").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main").ConfigureAwait(true);
        var incoming = await RemoteTipAsync().ConfigureAwait(true);

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var remote = new AuthenticatedGitRemote(sample.Remote, password);
        await using var server = await remote.StartAsync(token).ConfigureAwait(true);
        var url = new Uri(AuthenticatedGitRemote.RemoteUrl(server));
        await ConfigureCredentialStoreAsync(sample, url, token).ConfigureAwait(true);

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        await using var bridge = await connection.ConnectStdioAsync(Path.Combine(checkout, ".git", "auth-stdio.json")).ConfigureAwait(true);
        var root = await connection.AddRootAsync(checkout).ConfigureAwait(true);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = "" }).ConfigureAwait(true);
        var worktree = new Dictionary<string, object?>(StringComparer.Ordinal) { ["worktreePath"] = checkout };
        var remotes = await connection.CallAsync("git_list_remotes", worktree).ConfigureAwait(true);
        var target = new Dictionary<string, object?>(worktree, StringComparer.Ordinal)
        {
            ["name"] = "authenticated",
            ["revision"] = remotes.GetProperty("data").GetProperty("revision").GetString(),
        };

        await VerifyAuthenticatedReadAsync().ConfigureAwait(true);
        var signed = await CommitSignedAsync(sample, connection, bridge, token).ConfigureAwait(true);
        var push = SyncTarget(signed);
        var denialsBeforePush = remote.Denials;
        await SetCredentialAsync("invalid-fixture-password").ConfigureAwait(true);
        var deniedPush = await MutateAsync("git_push", push, bridge).ConfigureAwait(true);
        ExpectOutcome("git_push_failed", deniedPush);
        Assert.True(remote.Denials > denialsBeforePush, "The smart HTTP server did not receive the invalid push credential.");
        Assert.Equal(incoming, await RemoteTipAsync().ConfigureAwait(true));
        Assert.Equal(signed, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());
        await SetCredentialAsync(password).ConfigureAwait(true);
        ExpectOutcome("ok", await MutateAsync("git_push", push, bridge).ConfigureAwait(true));
        Assert.Equal(signed, await RemoteTipAsync().ConfigureAwait(true));

        async Task VerifyAuthenticatedReadAsync()
        {
            await SetCredentialAsync("invalid-fixture-password").ConfigureAwait(true);
            const string Tracking = "refs/remotes/authenticated/main";
            ExpectOutcome("git_fetch_failed", await MutateAsync("git_fetch", target).ConfigureAwait(true));
            Assert.NotEqual(0, (await sample.RunAsync(checkout, token, "show-ref", "--verify", Tracking).ConfigureAwait(true)).ExitCode);
            Assert.True(remote.Denials > 0, "The smart HTTP server did not receive the invalid credential.");

            await SetCredentialAsync(password).ConfigureAwait(true);
            ExpectOutcome("ok", await MutateAsync("git_fetch", target).ConfigureAwait(true));
            Assert.Equal(incoming, (await sample.GitAsync(checkout, token, "rev-parse", Tracking).ConfigureAwait(true)).Trim());
            Assert.Equal(baseTip, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());

            ExpectOutcome("ok", await MutateAsync("git_pull", SyncTarget(baseTip), bridge).ConfigureAwait(true));
            Assert.Equal(incoming, (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());
        }

        Dictionary<string, object?> SyncTarget(string head) => new(target, StringComparer.Ordinal)
        {
            ["branchName"] = "main", ["expectedHead"] = head, ["remoteBranch"] = "main",
        };

        Task SetCredentialAsync(string secret) => File.WriteAllTextAsync(Path.Combine(checkout, ".git", "fixture-credentials"),
            new UriBuilder(url) { UserName = "fixture", Password = secret }.Uri.AbsoluteUri + "\n", token);

        async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> arguments, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(true);
            var input = new Dictionary<string, object?>(arguments, StringComparer.Ordinal) { ["context"] = context.GetProperty("context") };
            return await connection.CallAsync(tool, input, transport).ConfigureAwait(true);
        }

        async Task<string> RemoteTipAsync() =>
            (await sample.GitAsync(sample.Remote, token, "rev-parse", "refs/heads/main").ConfigureAwait(true)).Trim();

        void ExpectOutcome(string expected, JsonElement result)
        {
            Assert.Equal(expected, result.GetProperty("code").GetString());
            Assert.False(result.GetRawText().Contains(password, StringComparison.Ordinal), "A fixture credential appeared in an MCP response.");
            Assert.DoesNotContain("invalid-fixture-password", result.GetRawText(), StringComparison.Ordinal);
        }
    }

    private static async Task ConfigureCredentialStoreAsync(GitWorktreeFixture sample, Uri url, CancellationToken token)
    {
        await sample.GitAsync(sample.Primary, token, "remote", "add", "authenticated", url.AbsoluteUri).ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "--add", "credential.helper", "").ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "--add", "credential.helper",
            "store --file=.git/fixture-credentials").ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "credential.interactive", "false").ConfigureAwait(false);
        await sample.GitAsync(sample.Primary, token, "config", "--local", "http.proxy", "").ConfigureAwait(false);
    }

    private static async Task<string> CommitSignedAsync(GitWorktreeFixture sample, GitWorkspaceStateTests.WorkspaceConnection connection,
        McpClient bridge, CancellationToken token)
    {
        var checkout = sample.Primary;
        var key = Path.Combine(checkout, ".git", "fixture-signing-key");
        var signers = Path.Combine(checkout, ".git", "fixture-allowed-signers");
        await CreateSigningKeyAsync(checkout, key, token).ConfigureAwait(false);
        var publicKey = await File.ReadAllTextAsync(key + ".pub", token).ConfigureAwait(false);
        await File.WriteAllTextAsync(signers, "fixture@example.invalid " + publicKey, token).ConfigureAwait(false);

        var hooks = Directory.CreateDirectory(Path.Combine(checkout, ".git", "fixture-empty-hooks")).FullName;
        foreach (var (name, value) in new (string, string)[]
        {
            ("user.name", "Retro IDE Fixture"), ("user.email", "fixture@example.invalid"),
            ("gpg.format", "ssh"), ("user.signingkey", key), ("gpg.ssh.allowedSignersFile", signers),
            ("commit.gpgsign", "true"), ("core.hooksPath", hooks),
        })
            await sample.GitAsync(checkout, token, "config", "--local", name, value).ConfigureAwait(false);

        const string PathToStage = "signed.txt";
        GitWorktreeFixture.Write(checkout, PathToStage, "Retro IDE signing fixture\n");
        await connection.ChangeAsync("git_stage_file", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["path"] = PathToStage,
        }).ConfigureAwait(false);
        var state = await connection.CallAsync("git_get_commit_state", new(StringComparer.Ordinal) { ["worktreePath"] = checkout }).ConfigureAwait(false);
        Assert.Equal("ok", state.GetProperty("code").GetString());
        var result = await connection.ChangeAsync("git_commit", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["preimage"] = state.GetProperty("data").GetProperty("preimage").GetString(),
            ["message"] = "Signed through Retro IDE",
        }, bridge).ConfigureAwait(false);
        var head = result.GetProperty("data").GetProperty("head").GetString();
        Assert.NotNull(head);
        await sample.GitAsync(checkout, token, "verify-commit", head).ConfigureAwait(false);
        return head;
    }

    private static async Task CreateSigningKeyAsync(string checkout, string key, CancellationToken token)
    {
        var start = new ProcessStartInfo("ssh-keygen")
        {
            WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-q", "-t", "ed25519", "-N", "", "-f", key })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start ssh-keygen.");
        var stderr = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var diagnostic = await stderr.ConfigureAwait(false);
            Assert.True(process.ExitCode == 0, diagnostic);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
