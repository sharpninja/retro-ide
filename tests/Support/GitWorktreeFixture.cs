using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RetroIde.Testing;

/// <summary>Creates an isolated native Git repository, a linked worktree, and a peer with a bare local remote.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos, ac-test-git-stage-and-push, ac-test-git-recovery. Add reproducible paths and a multi-hunk staging fixture without using the operator's Git configuration.</remarks>
internal sealed class GitWorktreeFixture : IDisposable
{
    /// <summary>Tracks a source path containing spaces, Unicode and Git pathspec punctuation.</summary>
    internal const string ChangedPath = "src/Café [demo] #1.cs";

    /// <summary>Tracks the shared path used to create a conflict in the primary worktree.</summary>
    internal const string ConflictPath = "shared source.txt";

    /// <summary>Provides the common-ancestor text before the peer and primary diverge.</summary>
    internal const string SeedConflict = "shared base\n";

    /// <summary>Tracks a source with independently selectable changes.</summary>
    internal const string HunkPath = "src/two changes.asm";

    /// <summary>Keeps the fixture's editable lines far enough apart to produce two native hunks.</summary>
    internal static readonly string SeedHunks = string.Join('\n', Enumerable.Range(1, 20)
        .Select(line => "line " + line.ToString(CultureInfo.InvariantCulture))) + '\n';

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-git-");
    private readonly string _git;
    private readonly string _hooks;

    /// <summary>Allocates a private directory and names the native Git checkouts beneath it.</summary>
    /// <param name="git">The executable that will run native Git commands.</param>
    private GitWorktreeFixture(string git)
    {
        _git = git;
        Primary = _root.CreateSubdirectory("primary checkout").FullName;
        Linked = Path.Combine(_root.FullName, "linked checkout");
        Peer = Path.Combine(_root.FullName, "peer checkout");
        Remote = Path.Combine(_root.FullName, "bare remote.git");
        _hooks = _root.CreateSubdirectory("empty hooks").FullName;
    }

    /// <summary>Gets the initial checkout and primary index.</summary>
    internal string Primary { get; }

    /// <summary>Gets the second worktree sharing the primary repository's object database.</summary>
    internal string Linked { get; }

    /// <summary>Gets an independently cloned peer checkout.</summary>
    internal string Peer { get; }

    /// <summary>Gets the path of the bare fixture remote.</summary>
    internal string Remote { get; }

    /// <summary>Initializes the repositories and their seeded history before returning the fixture.</summary>
    /// <param name="git">The executable or command name to invoke.</param>
    /// <param name="token">Cancellation for fixture setup.</param>
    /// <returns>The initialized fixture. The caller disposes it to remove its directories.</returns>
    internal static async Task<GitWorktreeFixture> CreateAsync(string git, CancellationToken token)
    {
        var fixture = new GitWorktreeFixture(git);
        try
        {
            await fixture.InitializeAsync(token).ConfigureAwait(false);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>Writes fixture content to a worktree, creating the parent directory when needed.</summary>
    /// <param name="repository">The owning fixture checkout.</param>
    /// <param name="path">The relative path within the checkout.</param>
    /// <param name="content">The file's complete content.</param>
    internal static void Write(string repository, string path, string content)
    {
        var full = Path.Combine(repository, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Writes a patch outside any Git checkout and returns its absolute path.</summary>
    /// <param name="content">The exact native Git diff selected for the fixture.</param>
    /// <returns>The fixture-local artifact path accepted by Git apply.</returns>
    internal string WritePatch(string content)
    {
        var patch = Path.Combine(_root.FullName, "selected hunk.patch");
        File.WriteAllText(patch, content);
        return patch;
    }

    /// <summary>Runs native Git and returns stdout when the command completes successfully.</summary>
    /// <param name="repository">The working directory captured for the command.</param>
    /// <param name="token">Cancellation for the Git process and output reads.</param>
    /// <param name="arguments">The exact arguments following fixture isolation options.</param>
    /// <returns>The decoded standard output.</returns>
    /// <exception cref="InvalidOperationException">Git exits unsuccessfully.</exception>
    internal async Task<string> GitAsync(string repository, CancellationToken token, params string[] arguments)
    {
        var result = await RunAsync(repository, token, arguments).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git {arguments[0]} returned {result.ExitCode}. {result.Error.Trim()} {result.Output.Trim()}");

        return result.Output;
    }

    /// <summary>Runs native Git with isolated configuration and captures its exit code and output streams.</summary>
    /// <param name="repository">The Git command's working directory.</param>
    /// <param name="token">Cancellation for the process and output reads.</param>
    /// <param name="arguments">The exact Git arguments to execute.</param>
    /// <returns>The exit status and decoded streams, including unsuccessful Git outcomes.</returns>
    internal async Task<GitResult> RunAsync(string repository, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(_git)
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root.FullName, "empty fixture config");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";
        string[] fixtureOptions =
        [
            "--no-pager", "-c", "user.name=Retro IDE Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", "-c", "protocol.file.allow=always",
            "-c", "core.hooksPath=" + _hooks,
        ];
        foreach (var argument in fixtureOptions.Concat(arguments))
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Seeds the literal-path, partial-hunk and conflict files and clones the independent peer.</summary>
    /// <param name="token">Cancellation for fixture setup.</param>
    private async Task InitializeAsync(CancellationToken token)
    {
        await GitAsync(Primary, token, "init", "--bare", "--initial-branch=main", Remote).ConfigureAwait(false);
        await GitAsync(Primary, token, "init", "--initial-branch=main").ConfigureAwait(false);
        Write(Primary, ChangedPath, "tracked base\n");
        Write(Primary, ConflictPath, SeedConflict);
        Write(Primary, HunkPath, SeedHunks);
        await GitAsync(Primary, token, "--literal-pathspecs", "add", "--", ChangedPath, ConflictPath, HunkPath).ConfigureAwait(false);
        await GitAsync(Primary, token, "commit", "-m", "Seed the worktree fixture").ConfigureAwait(false);
        await GitAsync(Primary, token, "remote", "add", "origin", Remote).ConfigureAwait(false);
        await GitAsync(Primary, token, "push", "--set-upstream", "origin", "main").ConfigureAwait(false);
        await GitAsync(Primary, token, "worktree", "add", "-b", "fixture-linked", Linked).ConfigureAwait(false);
        await GitAsync(Primary, token, "clone", "--no-local", Remote, Peer).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            foreach (var file in _root.EnumerateFiles("*", SearchOption.AllDirectories))
                if (file.IsReadOnly)
                    file.IsReadOnly = false;
        _root.Delete(recursive: true);
    }

    /// <summary>Contains a native Git process result.</summary>
    /// <param name="ExitCode">The Git process's exit code.</param>
    /// <param name="Output">Git's UTF-8 standard output.</param>
    /// <param name="Error">Git's UTF-8 standard error.</param>
    internal readonly record struct GitResult(int ExitCode, string Output, string Error);
}
