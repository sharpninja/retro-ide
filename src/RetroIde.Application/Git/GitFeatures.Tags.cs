namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitTagOperations.ReadInput> InspectTag { get; } = new("git.tag.read", "git_get_tag",
        "Inspect a native Git tag and its object, target and annotation.", GitTagOperations.InspectAsync, typeof(GitTagOperations.Tag));

    public Feature<GitTagOperations.CreateInput> CreateTag { get; } = new("git.tag.create", "git_create_tag",
        "Create a Git tag at a full commit ID, with an optional annotation. Uses the native signing configuration.",
        GitTagOperations.CreateAsync, typeof(GitTagOperations.Tag), mutation: true);

    public Feature<GitTagOperations.DeleteInput> DeleteTag { get; } = new("git.tag.delete", "git_delete_tag",
        "Delete a local Git tag only when it still points to the reviewed object ID.",
        GitTagOperations.DeleteAsync, typeof(GitTagOperations.Tag), mutation: true);
}
