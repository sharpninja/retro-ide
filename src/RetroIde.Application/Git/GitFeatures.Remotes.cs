namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitRemoteOperations.ListInput> Remotes { get; } = new("git.remotes.read", "git_list_remotes",
        "Read the selected worktree's remotes without returning embedded credentials.",
        GitRemoteOperations.ListAsync, typeof(GitRemoteOperations.RemoteList));

    public Feature<GitRemoteOperations.ConfigureInput> AddRemote { get; } = Configure(
        "git.remote.add", "git_add_remote", "Add a Git remote to the reviewed repository configuration.", GitRemoteOperations.Change.Add);

    public Feature<GitRemoteOperations.ConfigureInput> SetRemoteUrl { get; } = Configure(
        "git.remote.url", "git_set_remote_url", "Change the selected Git remote's fetch or push URL.", GitRemoteOperations.Change.SetUrl);

    public Feature<GitRemoteOperations.ConfigureInput> RenameRemote { get; } = Configure(
        "git.remote.rename", "git_rename_remote", "Rename a reviewed Git remote and its tracking refs.", GitRemoteOperations.Change.Rename);

    public Feature<GitRemoteOperations.ConfigureInput> RemoveRemote { get; } = Configure(
        "git.remote.remove", "git_remove_remote", "Remove a reviewed Git remote and its tracking refs.", GitRemoteOperations.Change.Remove);

    public Feature<GitRemoteOperations.FetchInput> Fetch { get; } = new("git.remote.fetch", "git_fetch",
        "Fetch the reviewed remote tracking refs without integrating changes into the selected branch.",
        GitRemoteOperations.FetchAsync, typeof(GitRemoteOperations.FetchResult), mutation: true);

    public Feature<GitRemoteOperations.UpstreamInput> SetUpstream { get; } = new("git.branch.upstream", "git_set_upstream",
        "Set or clear a reviewed local branch's upstream. Use the full ref name.",
        GitRemoteOperations.SetUpstreamAsync, typeof(GitRemoteOperations.UpstreamResult), mutation: true);

    private static Feature<GitRemoteOperations.ConfigureInput> Configure(string id, string tool, string summary, GitRemoteOperations.Change change) =>
        new(id, tool, summary, (input, view, token) => GitRemoteOperations.ConfigureAsync(input, view, change, token),
            typeof(GitRemoteOperations.RemoteList), mutation: true);
}
