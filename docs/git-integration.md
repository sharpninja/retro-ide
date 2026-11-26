# Git integration

Phase 03 defines a Git menu, a dockable Source Control pane, and Git actions in Project Explorer and the editor. The design supports several repositories and linked worktrees in one workspace. The active editor and tree selection suggest a repository context, and the Source Control header identifies the selected repository, worktree, and branch or detached commit. The user can choose another repository explicitly. [Project Explorer](architecture.md#project-explorer)

## Workspace and worktree context

Discover repositories for project and folder roots. Opening a nested repository or a submodule gives it its own context. A linked worktree has its own working files, index, and checked-out state and shares repository data with its sibling worktrees. Persist the selected context with the workspace. Clone and Init can add their resulting folders as workspace roots. A folder without a repository remains browsable in Project Explorer. [Git worktrees](https://git-scm.com/docs/git-worktree)

Project Explorer decorates tracked, untracked, ignored, modified, staged, and conflicted files. The Source Control pane groups changes by worktree, index, and conflict state. Selecting a change opens the relevant diff tab. The editor can show inline changes and navigate to blame or the file's history.

## Workflows

| Area | IDE commands and views |
| --- | --- |
| Repositories and configuration | Clone, Init, open existing repositories, select a repository or worktree, inspect and edit remotes and upstream tracking, expose ignored files and edit ignore rules. Show authentication or repository errors with the affected context. |
| Changes and index | Review working-tree and staged diffs, including renames and binary changes. Stage and unstage selected files or hunks. Restore selected content and distinguish index changes from working-file changes. |
| Commits and history | Commit staged changes, amend, inspect commit graph and details, review file history and blame, compare refs or commits, and create or inspect tags. Git applies configured hooks and signing. |
| Branches and remote communication | Create, switch, rename, and remove branches. Fetch, pull with the selected integration strategy, push, set an upstream, and report rejected updates or divergence. Force-with-lease is an explicit push variant. |
| Integration and recovery | Merge, rebase, cherry-pick, revert, and reset with explicit target and mode. Display the current operation and offer the actions Git allows, such as Continue, Skip, or Abort. Resolve file conflicts in a merge editor and stage the resolved file. |
| Stashes and patches | Create, inspect, apply, pop, and drop stashes. Apply or export patches with the selected index or worktree destination. |
| Worktrees, submodules, and LFS | List, add, open, lock, unlock, and remove linked worktrees. Show submodule status and support init, update, and sync in the selected repository. Honor configured Git LFS filters and hooks and expose LFS commands where Git LFS is installed. |
| Advanced commands | Open the integrated terminal in the selected worktree with the same native Git environment. It supports arbitrary Git commands and extensions, including reflog, bisect, notes, and recovery workflows. Refresh IDE state after terminal and external client changes. |

## Execution and state

Use the installed Git executable for repository operations on Windows and Linux. The IDE discovers Git or accepts a configured executable and reports its availability. The Source Control ViewModel registers with the main controller. Commands launch typed CQRS requests with the repository, worktree, and selected files or refs captured at invocation. The Git service invokes the executable asynchronously with explicit arguments and working directory. Its interactive commands use the user's Git configuration, HTTPS credential helpers, SSH agent, and signing environment. Credential prompts and signing tools use their interactive channels, and logs omit secrets. [Git credentials](https://git-scm.com/docs/gitcredentials)

- Read repository status from stable machine output such as `git status --porcelain=v2 -z --branch`. Parse NUL-delimited paths and change records. Pass literal paths or use NUL-delimited pathspec input where Git supports it. Resolve the worktree Git directory and common Git directory through Git so linked worktrees use the right context. [Git status porcelain format](https://git-scm.com/docs/git-status#_porcelain_format_version_2)
- Use Git and the filesystem as the source of truth. Refresh after a command and when relevant files, the index, or refs change outside the IDE. Coalesce duplicate refresh requests and apply results to their captured context. Preserve native Git locks and serialize overlapping IDE writes to the same index, working files, or shared refs.
- Capture the relevant staged or working content for hunk operations. Recheck it before applying a selected hunk and refresh the diff if it changed. Treat the working file, open editor buffer, and index as distinct states. Show external changes to a dirty editor so the user can reconcile them before replacing their edits.
- Detect ongoing operations started in the IDE or an external client. The merge editor maps Git's base and conflict stages to the specific merge, rebase, or cherry-pick. Selecting a resolution edits the file. Staging records the resolved file, and the operation's Continue or Abort command follows native Git behavior.
- Return structured outcomes with the command, repository context, exit status, and relevant Git diagnostics. Refresh state after errors and cancellation before enabling follow-up actions. When an operation logically blocks interaction in a scope, display the workbench's system wait cursor over that scope.

## Verification

Use isolated repositories and local bare remotes on Windows and Linux to verify discovery, linked-worktree context, status and unusual paths, staged and unstaged diffs, hunks, branches, history, remote synchronization, conflicts, and operation recovery. Include externally initiated changes, failed or cancelled requests, and unsaved editor buffers. Verify credentials, signing, submodules, worktrees, and LFS with controlled fixtures on their supported hosts. Track the work in `FR-RETRO-013`, `TR-RETRO-GIT-013`, and `TEST-RETRO-013`.
