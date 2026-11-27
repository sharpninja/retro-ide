namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitSubmoduleOperations.ListInput> ListSubmodules { get; } = new("git.submodule.list", "git_list_submodules",
        "List native Git submodule paths, index commits, checkout state and review revisions in the selected repository.",
        GitSubmoduleOperations.ListAsync, typeof(GitSubmoduleState.Module[]));

    public Feature<GitSubmoduleOperations.TargetInput> InitSubmodule { get; } = new("git.submodule.init", "git_init_submodule",
        "Initialize local native Git configuration for one reviewed submodule.",
        static (input, view, token) => GitSubmoduleOperations.RunAsync(input, GitSubmoduleOperations.Operation.Init, view, token),
        typeof(GitSubmoduleState.Module), mutation: true);

    public Feature<GitSubmoduleOperations.TargetInput> UpdateSubmodule { get; } = new("git.submodule.update", "git_update_submodule",
        "Initialize and check out the index commit of one reviewed submodule while protecting local changes and open buffers.",
        static (input, view, token) => GitSubmoduleOperations.RunAsync(input, GitSubmoduleOperations.Operation.Update, view, token),
        typeof(GitSubmoduleState.Module), mutation: true);

    public Feature<GitSubmoduleOperations.TargetInput> SyncSubmodule { get; } = new("git.submodule.sync", "git_sync_submodule",
        "Sync the local URL for one reviewed initialized submodule from its Git configuration file.",
        static (input, view, token) => GitSubmoduleOperations.RunAsync(input, GitSubmoduleOperations.Operation.Sync, view, token),
        typeof(GitSubmoduleState.Module), mutation: true);
}
