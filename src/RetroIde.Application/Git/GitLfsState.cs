using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.Git;

internal static class GitLfsState
{
    internal sealed record FileEntry(string Path, string ObjectId, long Size, bool IsMaterialized, bool IsDownloaded);
    internal sealed record Status(GitWorktree Repository, bool IsInstalled, bool HasFilter, string Revision, FileEntry[] Files,
        string[] Remotes, string? Version = null, string? Head = null);

    internal static async Task<Status> ReadAsync(GitWorktree repo, CancellationToken token)
    {
        var nativeVersion = await ReadAsync(repo, token, "lfs", "version").ConfigureAwait(false);
        var version = nativeVersion?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (version?.StartsWith("git-lfs/", StringComparison.Ordinal) != true)
            return new(repo, false, false, "", [], []);

        var head = await ReadHeadAsync(repo, token).ConfigureAwait(false);
        var remotes = (await ReadAsync(repo, token, "remote").ConfigureAwait(false)
            ?? throw new InvalidDataException("Git could not list the repository's remotes."))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var config = await ReadConfigAsync(repo, token).ConfigureAwait(false);
        var repositoryConfig = await ReadLfsConfigAsync(repo, token).ConfigureAwait(false);
        var files = head is null ? [] : await ReadFilesAsync(repo, head, token).ConfigureAwait(false);
        if (!string.Equals(await ReadHeadAsync(repo, token).ConfigureAwait(false), head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("HEAD changed during Git LFS inspection.");

        var keys = config.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var hasFilter = HasValue("filter.lfs.process") || HasValue("filter.lfs.clean") && HasValue("filter.lfs.smudge");
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { repo.WorktreePath, repo.GitDirectory, head, version, config, repositoryConfig, remotes, files }, WireJson.Options)));
        return new(repo, true, hasFilter, revision, files, remotes, version, head);

        bool HasValue(string key) => keys.Any(value => value.StartsWith(key + '\n', StringComparison.Ordinal) && value.Length > key.Length + 1);
    }

    internal static async Task<string?> ReadHeadAsync(GitWorktree repo, CancellationToken token)
    {
        var head = (await ReadAsync(repo, token, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false))?.Trim();
        return GitHistoryOperations.IsObjectId(head) ? head : null;
    }

    private static async Task<FileEntry[]> ReadFilesAsync(GitWorktree repo, string head, CancellationToken token)
    {
        var output = await ReadAsync(repo, token, "lfs", "ls-files", "--json", "--", head).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git LFS did not return its file inventory.");
        try
        {
            using var json = JsonDocument.Parse(output);
            var entries = json.RootElement.GetProperty("files");
            return entries.EnumerateArray().Select(entry =>
            {
                var path = entry.GetProperty("name").GetString();
                var oid = entry.GetProperty("oid").GetString();
                var size = entry.GetProperty("size").GetInt64();
                if (string.IsNullOrEmpty(path) || oid is not { Length: 64 } || !oid.All(Uri.IsHexDigit) || size < 0)
                    throw new InvalidDataException("Git LFS returned an invalid file entry.");
                return new FileEntry(path, oid, size, entry.GetProperty("checkout").GetBoolean(), entry.GetProperty("downloaded").GetBoolean());
            }).OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("Git LFS returned invalid file inventory.", error);
        }
    }

    private static async Task<string> ReadConfigAsync(GitWorktree repo, CancellationToken token)
    {
        var command = NativeGit.WorktreeArguments(repo);
        command.AddRange(["config", "--null", "--get-regexp", "^(lfs\\.|remote\\.|filter\\.lfs\\.)"]);
        var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, command, token).ConfigureAwait(false);
        return result.ExitCode switch
        {
            0 => new UTF8Encoding(false, true).GetString(result.Output),
            1 => "",
            _ => throw new InvalidDataException("Git LFS configuration could not be read."),
        };
    }

    private static async Task<string> ReadLfsConfigAsync(GitWorktree repo, CancellationToken token)
    {
        var result = await ReadAsync(repo, token, "config", "--null", "--no-includes", "--file", ".lfsconfig", "--list")
            .ConfigureAwait(false);
        return result ?? "";
    }

    private static Task<string?> ReadAsync(GitWorktree repo, CancellationToken token, params string[] args) =>
        NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. args], token);
}
