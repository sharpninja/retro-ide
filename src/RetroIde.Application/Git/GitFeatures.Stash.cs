namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitStash.ListInput> ListStashes { get; } = new("git.stash.list", "git_list_stashes",
        "Read the selected checkout's shared stash stack and review revision.", GitStash.ListAsync, typeof(GitStash.Stack));
    public Feature<GitStash.EntryInput> InspectStash { get; } = new("git.stash.inspect", "git_inspect_stash",
        "Read the patch for the reviewed stash entry.", GitStash.InspectAsync, typeof(GitStash.Inspection));
    public Feature<GitStash.CreateInput> CreateStash { get; } = new("git.stash.create", "git_create_stash",
        "Save current changes and optionally untracked files in a native Git stash.", GitStash.CreateAsync, typeof(GitStash.Result), mutation: true);
    public Feature<GitStash.EntryInput> ApplyStash { get; } = StashAction("git.stash.apply", "git_apply_stash",
        "Apply the reviewed stash and keep its entry.", GitStash.Action.Apply);
    public Feature<GitStash.EntryInput> PopStash { get; } = StashAction("git.stash.pop", "git_pop_stash",
        "Apply the reviewed stash and drop it after a successful apply.", GitStash.Action.Pop);
    public Feature<GitStash.EntryInput> DropStash { get; } = StashAction("git.stash.drop", "git_drop_stash",
        "Drop the reviewed stash entry from the stack.", GitStash.Action.Drop);

    private static Feature<GitStash.EntryInput> StashAction(string id, string tool, string description, GitStash.Action action) =>
        new(id, tool, description, (input, view, token) => GitStash.ChangeAsync(input, view, action, token), typeof(GitStash.Result), mutation: true);
}
