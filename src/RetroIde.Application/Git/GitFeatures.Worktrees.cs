namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitWorktreeOperations.ListInput> ListWorktrees { get; } = new("git.worktree.list", "git_list_worktrees",
        "List the selected repository's Git worktrees and their HEAD, branch, lock and prune state.",
        GitWorktreeOperations.ListAsync, typeof(GitHistoryOperations.Worktree[]));

    public Feature<GitWorktreeOperations.AddInput> AddWorktree { get; } = new("git.worktree.add", "git_add_worktree",
        "Add a linked checkout at an absolute destination and full commit ID, detached or on a local branch.",
        GitWorktreeOperations.AddAsync, typeof(GitHistoryOperations.Worktree), mutation: true);

    public Feature<GitWorktreeOperations.TargetInput> OpenWorktree { get; } = new("git.worktree.open", "git_open_worktree",
        "Open the reviewed Git worktree as an Explorer root and make it the active Git selection.",
        GitWorktreeOperations.OpenAsync, typeof(GitProvisionedWorkspace), mutation: true);

    public Feature<GitWorktreeOperations.TargetInput> LockWorktree { get; } = new("git.worktree.lock", "git_lock_worktree",
        "Lock the reviewed linked worktree with an optional reason.",
        static (input, view, token) => GitWorktreeOperations.MaintainAsync(input, GitWorktreeOperations.Maintenance.Lock, view, token),
        typeof(GitHistoryOperations.Worktree), mutation: true);

    public Feature<GitWorktreeOperations.TargetInput> UnlockWorktree { get; } = new("git.worktree.unlock", "git_unlock_worktree",
        "Unlock the reviewed linked worktree.",
        static (input, view, token) => GitWorktreeOperations.MaintainAsync(input, GitWorktreeOperations.Maintenance.Unlock, view, token),
        typeof(GitHistoryOperations.Worktree), mutation: true);

    public Feature<GitWorktreeOperations.TargetInput> RemoveWorktree { get; } = new("git.worktree.remove", "git_remove_worktree",
        "Remove the reviewed, unlocked and clean linked worktree while keeping its branch.",
        static (input, view, token) => GitWorktreeOperations.MaintainAsync(input, GitWorktreeOperations.Maintenance.Remove, view, token),
        typeof(GitHistoryOperations.Worktree), mutation: true);
}
