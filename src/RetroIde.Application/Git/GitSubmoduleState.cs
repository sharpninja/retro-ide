using System.Security.Cryptography;
using System.Text;

namespace RetroIde.Application.Git;

internal static class GitSubmoduleState
{
    private const string MetadataFile = ".gitmodules";
    private const string Prefix = "submodule.";
    private static readonly StringComparer NativePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal sealed record Module(string Path, string Revision, bool IsConfigured, bool HasCheckout, bool IsDirty,
        bool IsConflicted, bool CanChange, string? Name = null, string? IndexCommit = null, string? Head = null);
    internal sealed record Entry(Module Module, string? FullPath, GitWorktree? Checkout);
    private sealed record Link(string Path, string? Commit, bool IsConflicted);
    private sealed record Definition(string Name, string? Url);

    internal static async Task<Entry[]> ReadAsync(GitWorktree repo, CancellationToken token)
    {
        var index = await ReadGitAsync(repo, token, "ls-files", "--stage", "-z").ConfigureAwait(false)
            ?? throw new InvalidDataException("Git submodule index could not be read.");
        var (links, metadataConflicted) = ReadLinks(index);
        if (links.Length == 0)
            return [];

        var metadata = await ReadMetadataAsync(repo, token).ConfigureAwait(false);
        var definitions = ReadDefinitions(metadata);
        var configured = await ReadLocalUrlsAsync(repo, token).ConfigureAwait(false);
        var dirtyPaths = await ReadDirtyPathsAsync(repo, token).ConfigureAwait(false);
        var metadataId = Digest(metadata);
        var entries = new List<Entry>(links.Length);
        foreach (var link in links)
        {
            token.ThrowIfCancellationRequested();
            definitions.TryGetValue(link.Path, out var definition);
            var url = definition is not null ? configured.GetValueOrDefault(Prefix + definition.Name + ".url") : null;
            var root = ResolvePath(repo.WorktreePath, link.Path);
            var checkout = root is null ? null : await FindCheckoutAsync(repo, root, token).ConfigureAwait(false);
            var head = checkout is null ? null : (await ReadGitAsync(checkout, token, "rev-parse", "--verify", "--quiet", "HEAD")
                .ConfigureAwait(false))?.Trim();
            var conflicted = link.IsConflicted || metadataConflicted;
            var dirty = dirtyPaths.Contains(link.Path);
            var canChange = !conflicted && root is not null && definition?.Url is { Length: > 0 } &&
                link.Commit is not null && (Attributes(Path.Combine(root, ".git")) is null || checkout is not null);
            var revision = Digest(string.Join('\0', repo.WorktreePath, repo.GitDirectory, link.Path, link.Commit, head, checkout?.GitDirectory,
                definition?.Name, url, metadataId, conflicted.ToString(), dirty.ToString(), canChange.ToString()));
            entries.Add(new Entry(new Module(link.Path, revision, url is not null, checkout is not null, dirty,
                conflicted, canChange, definition?.Name, link.Commit, head), root, checkout));
        }
        return [.. entries];
    }

    private static (Link[] Links, bool MetadataConflicted) ReadLinks(string index)
    {
        var links = new Dictionary<string, Link>(StringComparer.Ordinal);
        var metadataConflicted = false;
        foreach (var record in index.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = record.IndexOf('\t');
            if (separator < 0)
                throw new InvalidDataException("Git returned an invalid index record.");
            var path = record[(separator + 1)..];
            if (!record.StartsWith("160000 ", StringComparison.Ordinal) && !string.Equals(path, MetadataFile, StringComparison.Ordinal))
                continue;
            var fields = record[..separator].Split(' ');
            if (fields is not [_, var commit, "0" or "1" or "2" or "3"] || !GitHistoryOperations.IsObjectId(commit))
                throw new InvalidDataException("Git returned incomplete submodule index metadata.");

            var merged = string.Equals(fields[2], "0", StringComparison.Ordinal);
            if (string.Equals(path, MetadataFile, StringComparison.Ordinal))
                metadataConflicted |= !merged;
            else
                links[path] = new Link(path, merged ? commit : null, !merged);
        }
        return ([.. links.Values], metadataConflicted);
    }

    private static async Task<string> ReadMetadataAsync(GitWorktree repo, CancellationToken token)
    {
        var file = Path.Combine(repo.WorktreePath, MetadataFile);
        if (Attributes(file) is { } attributes)
        {
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory))
                throw new InvalidDataException("Git submodule metadata is not a regular file.");
            return await ReadGitAsync(repo, token, "config", "--no-includes", "--null", "--file", file, "--list").ConfigureAwait(false)
                ?? throw new InvalidDataException("Git submodule metadata could not be parsed.");
        }

        // Git can obtain the file from the index even when a sparse checkout omits it.
        return await ReadGitAsync(repo, token, "config", "--no-includes", "--null", "--blob", ":.gitmodules", "--list")
            .ConfigureAwait(false) ?? "";
    }

    private static Dictionary<string, Definition> ReadDefinitions(string metadata)
    {
        var entries = ConfigEntries(metadata).ToLookup(pair => pair.Key, StringComparer.Ordinal);
        var definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);
        foreach (var pair in entries.Where(group => group.Key.StartsWith(Prefix, StringComparison.Ordinal) &&
            group.Key.EndsWith(".path", StringComparison.Ordinal)))
        {
            var path = pair.Single().Value;
            var prefix = pair.Key[..^".path".Length];
            var name = prefix[Prefix.Length..];
            var url = entries[prefix + ".url"].SingleOrDefault().Value;
            definitions.Add(path, new Definition(name, url));
        }
        return definitions;
    }

    private static async Task<Dictionary<string, string>> ReadLocalUrlsAsync(GitWorktree repo, CancellationToken token)
    {
        var arguments = NativeGit.WorktreeArguments(repo);
        arguments.AddRange(["config", "--local", "--no-includes", "--null", "--get-regexp", "^submodule\\..*\\.url$"]);
        var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, token).ConfigureAwait(false);
        if (result.ExitCode is not (0 or 1))
            throw new InvalidDataException("Git submodule configuration could not be read.");
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in ConfigEntries(new UTF8Encoding(false, true).GetString(result.Output)))
            urls[pair.Key] = pair.Value;
        return urls;
    }

    private static IEnumerable<KeyValuePair<string, string>> ConfigEntries(string output)
    {
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            if (separator > 0)
                yield return new(entry[..separator], entry[(separator + 1)..]);
        }
    }

    private static async Task<HashSet<string>> ReadDirtyPathsAsync(GitWorktree repo, CancellationToken token)
    {
        var status = await ReadGitAsync(repo, token, "status", "--porcelain=v2", "-z", "--branch", "--no-renames",
            "--untracked-files=normal", "--ignore-submodules=none").ConfigureAwait(false)
            ?? throw new InvalidDataException("Git submodule status could not be read.");
        return GitStatus.Parse(repo, status).Entries.Where(entry => entry.Submodule is { Length: 4 } flags &&
            (flags[2] == 'M' || flags[3] == 'U')).Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<GitWorktree?> FindCheckoutAsync(GitWorktree repo, string root, CancellationToken token)
    {
        if (!Directory.Exists(root) || Attributes(Path.Combine(root, ".git")) is not { } metadata ||
            metadata.HasFlag(FileAttributes.ReparsePoint))
            return null;
        var found = await NativeGit.DiscoverAsync(root, "", token).ConfigureAwait(false);
        if (found is null || !NativePaths.Equals(found.Repository.WorktreePath, root))
            return null;
        var parent = await ReadGitAsync(found.Repository, token, "rev-parse", "--path-format=absolute", "--show-superproject-working-tree")
            .ConfigureAwait(false);
        if (parent is null || !parent.EndsWith('\n'))
            return null;
        var path = parent[..^1];
        if (OperatingSystem.IsWindows() && path.EndsWith('\r'))
            path = path[..^1];
        return Path.IsPathFullyQualified(path) && NativePaths.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), repo.WorktreePath)
            ? found.Repository : null;
    }

    private static string? ResolvePath(string root, string path)
    {
        if (Path.IsPathRooted(path) || path.Split('/').Any(segment => segment is "" or "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return null;
        foreach (var segment in path.Split('/'))
        {
            root = Path.Combine(root, segment);
            if (Attributes(root) is { } attributes && (!attributes.HasFlag(FileAttributes.Directory) ||
                attributes.HasFlag(FileAttributes.ReparsePoint)))
                return null;
        }
        return root;
    }

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static Task<string?> ReadGitAsync(GitWorktree repo, CancellationToken token, params string[] options) =>
        NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. options], token);

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
