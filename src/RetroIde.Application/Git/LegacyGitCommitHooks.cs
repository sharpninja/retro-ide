namespace RetroIde.Application.Git;

/// <summary>Forwards native commit hooks on Git versions that only load hooks from files.</summary>
internal sealed class LegacyGitCommitHooks(string indexPath) : IDisposable
{
    private const string Forward = """
        hook=${0##*/}
        case "$hook" in
            reference-transaction|post-rewrite)
                input="$0.stdin"
                trap 'rm -f "$input"' 0
                cat > "$input" || exit 1
                git -c "core.hooksPath=$original_hooks" hook run --ignore-missing --to-stdin="$input" "$hook" -- "$@" || exit $?
                if [ "$hook" = reference-transaction ]; then
                    . "$GIT_INDEX_FILE.guard" < "$input"
                fi
                ;;
            *)
                exec git -c "core.hooksPath=$original_hooks" hook run --ignore-missing "$hook" -- "$@"
                ;;
        esac
        """;

    private static readonly string[] CommitHooks =
    [
        "pre-commit", "prepare-commit-msg", "commit-msg", "post-commit", "post-rewrite", "post-index-change",
    ];

    private readonly string _directory = indexPath + ".hooks";
    private bool _created;

    internal async Task<bool> PrepareAsync(GitWorktree repository, CancellationToken token)
    {
        var command = NativeGit.WorktreeArguments(repository);
        command.AddRange(["rev-parse", "--path-format=absolute", "--git-path", "hooks"]);
        var printed = await NativeGit.RunAsync(repository.WorktreePath, command, token).ConfigureAwait(false);
        if (printed is null || !printed.EndsWith('\n'))
            return false;
        var original = OperatingSystem.IsWindows() && printed.EndsWith("\r\n", StringComparison.Ordinal) ? printed[..^2] : printed[..^1];
        if (!Path.IsPathFullyQualified(original))
            return false;

        // Git's hook runner resolves and executes the original files with the original hook path.
        // The wrapper directory is unique to this private index and never enters repo config.
        Directory.CreateDirectory(_directory);
        _created = true;
        var quoted = "'" + original.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        var script = "#!/bin/sh\noriginal_hooks=" + quoted + "\n" + Forward.ReplaceLineEndings("\n") + "\n";
        await WriteAsync("reference-transaction").ConfigureAwait(false);
        foreach (var hook in CommitHooks.Where(name => File.Exists(Path.Combine(original, name))))
            await WriteAsync(hook).ConfigureAwait(false);
        return true;

        async Task WriteAsync(string name)
        {
            var path = Path.Combine(_directory, name);
            await File.WriteAllTextAsync(path, script, token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal void Configure(List<string> command) => command.AddRange(["-c", "core.hooksPath=" + _directory]);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_created)
            Directory.Delete(_directory, recursive: true);
    }
}
