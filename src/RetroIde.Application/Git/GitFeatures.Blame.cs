namespace RetroIde.Application.Git;

internal sealed partial class GitFeatures
{
    public Feature<GitBlameOperations.Input> Blame { get; } = new("git.blame.read", "git_get_blame",
        "Attribute lines of a selected checkout's working file or a file at a full commit ID.",
        static (input, view, token) => GitBlameOperations.ReadAsync(input, view, openTab: false, token), typeof(GitBlameOperations.Blame));

    public Feature<GitBlameOperations.Input> OpenBlame { get; } = new("git.blame.open", "git_open_blame",
        "Open or refresh the read-only Git blame tab for a selected file, revision and line range.",
        static (input, view, token) => GitBlameOperations.ReadAsync(input, view, openTab: true, token), typeof(GitBlameOperations.Tab), mutation: true);

    public Feature<GitBlameOperations.CloseInput> CloseBlame { get; } = new("git.blame.close", "git_close_blame",
        "Close a Git blame tab in the attached workbench.", mutation: true,
        static (input, view) => view is IGitRegistration git && git.CloseBlame(input.ViewKey)
            ? new("ok", "Git blame tab closed.")
            : new("view_closed", "The Git blame tab is not open in this workbench."));
}
