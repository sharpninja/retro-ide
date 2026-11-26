namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitPatchOperations.ExportInput> ExportPatch { get; } = new("git.patch.export", "git_export_patch",
        "Export the selected index or tracked worktree diff as native Git patch bytes.", GitPatchOperations.ExportAsync, typeof(GitPatchOperations.Export));
    public Feature<GitPatchOperations.ReviewInput> ReviewPatch { get; } = new("git.patch.review", "git_review_patch",
        "Check Git patch bytes against one layer of the selected worktree.", GitPatchOperations.ReviewAsync, typeof(GitPatchOperations.Review));
    public Feature<GitPatchOperations.ApplyInput> ApplyPatch { get; } = new("git.patch.apply", "git_apply_patch",
        "Apply reviewed Git patch bytes to one layer of the selected worktree.", GitPatchOperations.ApplyAsync, typeof(GitPatchOperations.Result), mutation: true);
}
