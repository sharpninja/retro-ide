namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitLfsOperations.StatusInput> LfsStatus { get; } = new("git.lfs.status", "git_lfs_status",
        "Read native Git LFS availability, filter configuration, current committed file inventory, cache status and remotes.",
        GitLfsOperations.StatusAsync, typeof(GitLfsState.Status));

    public Feature<GitLfsOperations.ActionInput> FetchLfs { get; } = new("git.lfs.fetch", "git_lfs_fetch",
        "Fetch Git LFS objects for the reviewed commit from a configured remote, leaving working files untouched.",
        static (input, view, token) => GitLfsOperations.RunAsync(input, GitLfsOperations.Operation.Fetch, view, token),
        typeof(GitLfsState.Status), mutation: true);

    public Feature<GitLfsOperations.ActionInput> CheckoutLfs { get; } = new("git.lfs.checkout", "git_lfs_checkout",
        "Check out cached Git LFS objects in the reviewed worktree while protecting open editor buffers.",
        static (input, view, token) => GitLfsOperations.RunAsync(input, GitLfsOperations.Operation.Checkout, view, token),
        typeof(GitLfsState.Status), mutation: true);

    public Feature<GitLfsOperations.ActionInput> PullLfs { get; } = new("git.lfs.pull", "git_lfs_pull",
        "Fetch Git LFS objects for the reviewed commit from a configured remote, then check out the available files.",
        static (input, view, token) => GitLfsOperations.RunAsync(input, GitLfsOperations.Operation.Pull, view, token),
        typeof(GitLfsState.Status), mutation: true);
}
