namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitOperationRecovery.ReadInput> Operation { get; } = new("git.operation.read", "git_get_operation",
        "Read the selected worktree's paused Git operation, unresolved paths, and recovery revision.",
        GitOperationRecovery.ReadAsync, typeof(GitOperationRecovery.State));

    public Feature<GitOperationRecovery.RecoverInput> ContinueOperation { get; } = new("git.operation.continue", "git_continue_operation",
        "Continue the reviewed native Git operation once conflicts are resolved and staged.",
        static (input, view, token) => GitOperationRecovery.RecoverAsync(input, view, GitOperationRecovery.Action.Continue, token),
        typeof(GitOperationRecovery.State), mutation: true);

    public Feature<GitOperationRecovery.RecoverInput> AbortOperation { get; } = new("git.operation.abort", "git_abort_operation",
        "Abort the reviewed native Git operation. Git restores the state preceding that operation.",
        static (input, view, token) => GitOperationRecovery.RecoverAsync(input, view, GitOperationRecovery.Action.Abort, token),
        typeof(GitOperationRecovery.State), mutation: true);

    public Feature<GitOperationRecovery.RecoverInput> SkipOperation { get; } = new("git.operation.skip", "git_skip_operation",
        "Skip the current commit in a reviewed rebase, cherry-pick, or revert and resume the remaining sequence.",
        static (input, view, token) => GitOperationRecovery.RecoverAsync(input, view, GitOperationRecovery.Action.Skip, token),
        typeof(GitOperationRecovery.State), mutation: true);

    public Feature<GitOperationStart.Input> StartOperation { get; } = new("git.operation.start", "git_start_operation",
        "Start a merge, rebase, cherry-pick, or revert from the reviewed checkout and commit.",
        GitOperationStart.StartAsync, typeof(GitOperationStart.Result), mutation: true);

    public Feature<GitReset.ReviewInput> ReviewReset { get; } = new("git.reset.review", "git_review_reset",
        "Review HEAD, index and worktree effects before resetting the selected checkout to the commit.",
        GitReset.ReviewAsync, typeof(GitReset.Review));

    public Feature<GitReset.ResetInput> Reset { get; } = new("git.reset.commit", "git_reset",
        "Apply the selected soft, mixed or hard reset against a current reviewed revision.",
        GitReset.ResetAsync, typeof(GitReset.Result), mutation: true);
}
