using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Reads and edits native remote settings and fetches the reviewed remote without switching or integrating.</summary>
internal static partial class GitRemoteOperations
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    internal sealed record ListInput(string WorktreePath);
    internal sealed record ConfigureInput(string WorktreePath, string Name, string Revision, string? Value = null, bool Push = false);
    internal sealed record FetchInput(string WorktreePath, string Name, string Revision, bool Prune = false);
    internal sealed record Remote(string Name, string[] FetchUrls, string[] PushUrls);
    internal sealed record RemoteList(GitWorktree Repository, string Revision, Remote[] Remotes);
    internal sealed record TrackingStatus(string Branch, string? Upstream = null, int? Ahead = null, int? Behind = null);
    internal sealed record FetchResult(GitWorktree Repository, string Name, TrackingStatus? Status = null);
    internal enum Change { Add, SetUrl, Rename, Remove }

    internal static Task<FeatureOutcome> ListAsync(ListInput input, IWorkbenchRegistration view, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var snapshot = await ReadConfigAsync(selection.Repository, token).ConfigureAwait(false);
            return Finish(selection, () => FeatureOutcome.Success("Git remotes read.", snapshot.Display));
        }, token);

    internal static Task<FeatureOutcome> ConfigureAsync(ConfigureInput input, IWorkbenchRegistration view, Change change, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var repo = selection.Repository;
            var snapshot = await ReadConfigAsync(repo, token).ConfigureAwait(false);
            if (!Matches(snapshot, input.Revision))
                return Changed();

            var problem = await ValidateChangeAsync(repo, snapshot, input, change, token).ConfigureAwait(false);
            if (problem is not null)
                return problem;
            return Authorize(selection, async cancellation =>
            {
                var current = await ReadConfigAsync(repo, cancellation).ConfigureAwait(false);
                if (!Matches(current, input.Revision))
                    return Changed();

                string[] command = change switch
                {
                    Change.Add => ["remote", "add", "--", input.Name, input.Value!],
                    Change.SetUrl when input.Push => ["remote", "set-url", "--push", "--", input.Name, input.Value!],
                    Change.SetUrl => ["remote", "set-url", "--", input.Name, input.Value!],
                    Change.Rename => ["remote", "rename", "--", input.Name, input.Value!],
                    _ => ["remote", "remove", "--", input.Name],
                };
                cancellation.ThrowIfCancellationRequested();
                var result = await RunAsync(repo, CancellationToken.None, command).ConfigureAwait(false);
                if (result.ExitCode != 0)
                    return new("git_remote_config_failed", "Git could not update the selected remote.");
                return FeatureOutcome.Success("Git remote settings updated.",
                    (await ReadConfigAsync(repo, CancellationToken.None).ConfigureAwait(false)).Display);
            });
        }, token);

    internal static Task<FeatureOutcome> FetchAsync(FetchInput input, IWorkbenchRegistration view, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var repo = selection.Repository;
            var snapshot = await ReadConfigAsync(repo, token).ConfigureAwait(false);
            if (!Matches(snapshot, input.Revision))
                return Changed();
            if (!snapshot.Remotes.TryGetValue(input.Name, out var remote))
                return new("git_remote_unavailable", "The selected Git remote is unavailable.");
            if (!remote.FetchMappings.All(IsTrackingRefspec))
                return new("git_fetch_refspec_unsupported", "Fetch must target remote tracking refs or tags.");

            return Authorize(selection, async cancellation =>
            {
                if (!Matches(await ReadConfigAsync(repo, cancellation).ConfigureAwait(false), input.Revision))
                    return Changed();
                var command = new List<string> { "-c", "protocol.ext.allow=never", "fetch", "--atomic", "--no-recurse-submodules" };
                if (input.Prune)
                    command.Add("--prune");
                command.AddRange(["--", input.Name]);
                var result = await RunAsync(repo, cancellation, [.. command]).ConfigureAwait(false);
                if (result.ExitCode != 0)
                    return new("git_fetch_failed", "Git fetch failed. Check the remote location and Git authentication.");

                var status = await NativeGit.ReadStatusAsync(selection, CancellationToken.None).ConfigureAwait(false);
                var tracking = status is null ? null : new TrackingStatus(status.Branch, status.Upstream, status.Ahead, status.Behind);
                return FeatureOutcome.Success("Git remote fetched.", new FetchResult(repo, input.Name, tracking));
            });
        }, token);

    private static async Task<FeatureOutcome?> ValidateChangeAsync(GitWorktree repo, ConfigSnapshot state, ConfigureInput input,
        Change change, CancellationToken token)
    {
        if (!await ValidNameAsync(repo, input.Name, token).ConfigureAwait(false))
            return new("git_remote_name_invalid", "Enter a valid remote name.");

        var exists = state.Remotes.ContainsKey(input.Name);
        if (change == Change.Add ? exists : !exists)
            return exists ? new("git_remote_exists", "A remote already has that name.") :
                new("git_remote_unavailable", "The selected Git remote is unavailable.");
        if (change is Change.Add or Change.SetUrl && !ValidEndpoint(input.Value))
            return new("git_remote_url_invalid", "Enter a Git URL or local repository path without embedded credentials.");
        if (change == Change.Rename)
        {
            if (!await ValidNameAsync(repo, input.Value, token).ConfigureAwait(false))
                return new("git_remote_name_invalid", "Enter a valid remote name.");
            if (state.Remotes.ContainsKey(input.Value!))
                return new("git_remote_exists", "A remote already has that name.");
        }
        return null;
    }

    private static async Task<ConfigSnapshot> ReadConfigAsync(GitWorktree repo, CancellationToken token)
    {
        var config = await RunAsync(repo, token, "config", "--includes", "--null", "--get-regexp", "^(remote|url)\\.").ConfigureAwait(false);
        if (config.ExitCode is not (0 or 1))
            throw new InvalidDataException("Git could not read remote settings.");
        var data = new UTF8Encoding(false, true).GetString(config.Output);
        var remotes = new SortedDictionary<string, ConfiguredRemote>(StringComparer.Ordinal);
        foreach (var value in data.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = value.IndexOf('\n');
            var key = separator < 0 ? value : value[..separator];
            var property = key.LastIndexOf('.');
            const string Prefix = "remote.";
            if (!key.StartsWith(Prefix, StringComparison.Ordinal) || property < Prefix.Length)
                continue;

            var name = key[Prefix.Length..property];
            if (!remotes.TryGetValue(name, out var remote))
                remotes.Add(name, remote = new ConfiguredRemote());
            var setting = separator < 0 ? "" : value[(separator + 1)..];
            switch (key[(property + 1)..])
            {
                case "url": remote.FetchUrls.Add(SafeLocation(setting)); break;
                case "pushurl": remote.PushUrls.Add(SafeLocation(setting)); break;
                case "fetch": remote.FetchMappings.Add(setting); break;
            }
        }
        var revision = Convert.ToHexString(HMACSHA256.HashData(RevisionKey, Encoding.UTF8.GetBytes(repo.CommonDirectory + '\0' + data)));
        var display = remotes.Select(pair => new Remote(pair.Key, [.. pair.Value.FetchUrls], [.. pair.Value.PushUrls])).ToArray();
        return new(new RemoteList(repo, revision, display), remotes);
    }

    private static bool ValidEndpoint(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith('-') && !value.Contains('\0') &&
        !value.StartsWith("ext::", StringComparison.OrdinalIgnoreCase) &&
        (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsFile ||
            ((!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrEmpty(uri.UserInfo)) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)));

    private static string SafeLocation(string value)
    {
        if (value.Contains("::", StringComparison.Ordinal) && !value.Contains("://", StringComparison.Ordinal))
            return "(custom transport)";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Host.Length > 0)
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return value.Contains("://", StringComparison.Ordinal) ? "(remote location)" : value;
    }

    private static bool IsTrackingRefspec(string spec)
    {
        var separator = spec.LastIndexOf(':');
        if (separator < 0 || separator == spec.Length - 1)
            return true;
        var target = spec.AsSpan(separator + 1);
        return target.StartsWith("refs/remotes/", StringComparison.Ordinal) || target.StartsWith("refs/tags/", StringComparison.Ordinal);
    }

    private static Task<bool> ValidNameAsync(GitWorktree repo, string? name, CancellationToken token) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Contains('\0')
            ? Task.FromResult(false)
            : SuccessfulAsync(repo, token, "check-ref-format", "refs/remotes/" + name + "/HEAD");

    private static async Task<bool> SuccessfulAsync(GitWorktree repository, CancellationToken token, params string[] args) =>
        (await RunAsync(repository, token, args).ConfigureAwait(false)).ExitCode == 0;

    private static bool Matches(ConfigSnapshot snapshot, string revision) => string.Equals(snapshot.Display.Revision, revision, StringComparison.Ordinal);
    private static FeatureOutcome Changed() => new("git_remote_changed", "The remote settings changed. Refresh the remote list and try again.");

    private sealed record ConfigSnapshot(RemoteList Display, SortedDictionary<string, ConfiguredRemote> Remotes);
    private sealed class ConfiguredRemote
    {
        public List<string> FetchUrls { get; } = [];
        public List<string> PushUrls { get; } = [];
        public List<string> FetchMappings { get; } = [];
    }

    private static async Task<FeatureOutcome> InScopeAsync(string path, IWorkbenchRegistration view,
        Func<GitSelection, Task<FeatureOutcome>> operation, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(path, selection.Repository.WorktreePath, comparison))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!string.Equals(selection.ScopeRoot, selection.Repository.WorktreePath, comparison))
            return new("git_remote_scope_required", "Open the worktree root before managing its remotes.");
        return await TryAsync(async () =>
        {
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            return found is null || !NativeGit.IsSameWorktree(found.Repository, selection.Repository)
                ? new("git_repository_unavailable", "The selected Git worktree is unavailable.")
                : await operation(selection).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static FeatureOutcome Finish(GitSelection expected, Func<FeatureOutcome> result) => new("ok", "Git remote operation prepared.")
    {
        Commit = view => view is IGitRegistration git && ReferenceEquals(git.SelectedGit, expected)
            ? result() : new("git_selection_changed", "The selected Git worktree changed."),
    };

    private static FeatureOutcome Authorize(GitSelection expected, Func<CancellationToken, Task<FeatureOutcome>> action) =>
        Finish(expected, () => new FeatureOutcome("ok", "Git remote operation authorized.")
        {
            ExecuteAuthorizedAsync = token => TryAsync(() => action(token)),
        });

    private static async Task<FeatureOutcome> TryAsync(Func<Task<FeatureOutcome>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new("git_remote_unavailable", "Git could not complete the remote operation.");
        }
    }

    private static Task<NativeGit.CommandResult> RunAsync(GitWorktree repo, CancellationToken token, params string[] command)
    {
        var arguments = NativeGit.WorktreeArguments(repo);
        arguments.AddRange(command);
        return NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, token);
    }
}
