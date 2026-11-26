namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitOperationRecovery.ReadInput> Operation { get; } = new("git.operation.read", "git_get_operation",
        "Read the selected worktree's paused Git operation, unresolved paths, and recovery revision.",
        GitOperationRecovery.ReadAsync, typeof(GitOperationRecovery.State));

    public Feature<GitOperationRecovery.RecoverInput> ContinueOperation { get; } = new("git.operation.continue", "git_continue_operation",
        "Continue the reviewed native Git operation once conflicts are resolved and staged.",
        static (input, view, token) => GitOperationRecovery.RecoverAsync(input, view, abort: false, token),
        typeof(GitOperationRecovery.State), mutation: true);

    public Feature<GitOperationRecovery.RecoverInput> AbortOperation { get; } = new("git.operation.abort", "git_abort_operation",
        "Abort the reviewed native Git operation. Git restores the state preceding that operation.",
        static (input, view, token) => GitOperationRecovery.RecoverAsync(input, view, abort: true, token),
        typeof(GitOperationRecovery.State), mutation: true);

    public Feature<GitOperationStart.Input> StartOperation { get; } = new("git.operation.start", "git_start_operation",
        "Start a merge, rebase, cherry-pick, or revert from the reviewed checkout and commit.",
        GitOperationStart.StartAsync, typeof(GitOperationStart.Result), mutation: true);
}
