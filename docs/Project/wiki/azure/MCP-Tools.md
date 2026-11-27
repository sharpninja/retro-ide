# Retro IDE MCP tools

Every supported Retro IDE feature has a Model Context Protocol (MCP) tool. CLI and desktop agents can inspect state and invoke the same operations available through the workbench. Use the official MCP C# SDK and Microsoft hosting extensions. `ModelContextProtocol` provides server registration and stdio, and `ModelContextProtocol.AspNetCore` provides Streamable HTTP. Use `ModelContextProtocol.Extensions.Tasks` for long-running operations when the client negotiates it. [Microsoft .NET SDK guidance](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)

## Phase 01 tool inventory

Phase 01 exposes `workbench_get_context` and `workbench_focus_tool`. They share one local IDE instance through Streamable HTTP or its stdio bridge. The four public context members are `viewKey`, `workspaceId`, `activeToolId`, and `workbenchRevision`. A missing selection is an explicit null. Mutations require a live registration, the current context and a write-enabled attachment. The rest of this page describes the broader implementation contract. [Cross-platform verification](receipts/phase-01-workbench-mcp.md)

## Phase 02 tool inventory

The display language, Project Explorer, and source editor operations below passed cross-platform verification and independent Phase 02 review. [Phase 02 verification](receipts/phase-02-workbench-explorer-editor.md)

### Display language

Use `workbench_get_display_language` to read the active locale, right-to-left direction, and installed locale tags. A write-enabled attachment can call `workbench_set_display_language` with a BCP 47 `locale` and the current workbench `context`. The change updates open windows and persists the selection in the IDE application data directory.

## Project Explorer tools

The Phase 02 implementation currently exposes these Project Explorer operations over the same stdio and Streamable HTTP attachments.

| Tool | Arguments | Result |
| --- | --- | --- |
| `workspace_open_root` | Absolute `path`, `kind` (`folder` or `project`), current workbench `context` | The stable root ID, kind, and path in `data`. |
| `workspace_list_roots` | None | The current workspace's roots in `data`. |
| `workspace_list_children` | `rootId`, `relativePath` (empty for the root) | The branch's workspace, parent, availability, and immediate children in `data`. |
| `workspace_set_expanded` | `rootId`, `relativePath`, `isExpanded`, current workbench `context` | The updated workbench context. The IDE saves the expansion with the workspace. |
| `workspace_select_entry` | `rootId`, `relativePath`, current workbench `context` | The updated workbench context. The IDE saves the root-relative selection with the workspace. |
| `workspace_open_file` | `rootId`, `relativePath`, optional one-based `position` (line and column), current workbench `context` | The opened or reused tab's `viewKey`, `workspaceId`, `documentId`, `rootId`, `relativePath`, and `revision` in `data`. |
| `workspace_reveal_entry` | `rootId`, `relativePath`, current workbench `context` | The updated workbench context after ancestors are expanded and the entry is selected. |
| `editor_save_document` | `document` (the tab context returned by opening), current workbench `context` | The saved tab's current context in `data`. Save checks the captured revision before writing. |
| `editor_get_document` | Tab `viewKey` | The current public document context in `data`. |
| `editor_find_text` | `document`, literal `query`, optional `matchCase` and `backward`, workbench `context` | Selects the next or previous match, wrapping at the end. Returns the match offset and length. |
| `editor_replace_text` | `document`, literal `query`, `replacement`, optional `matchCase` and `all`, workbench `context` | Replaces one or all matches as one undo step and returns the updated document context. |
| `editor_edit_text` | `document`, zero-based UTF-16 `offset`, range `length`, replacement `text`, workbench `context` | Applies the edit and returns the updated document context. |
| `editor_undo`, `editor_redo` | `document`, workbench `context` | Applies one edit history entry for the specified tab. |
| `editor_close_document` | `document`, optional `discardChanges`, workbench `context` | Closes the tab. A dirty tab remains open unless discard is explicit. |

Read-only attachments can list workspace roots and children and read a live document context. Root, expansion, selection, and editor mutations require a write-enabled attachment and the current public workbench context. Each branch read is scoped to an open root. Refresh the context after a successful mutation before sending the next write.

## Phase 03 Git tools

Phase 03 exposes native Git actions and the repository terminal through the same attached workbench over stdio and Streamable HTTP. Discovery takes a literal path within an open Project Explorer root. Use an empty `relativePath` to inspect the root itself. Use forward slashes in path arguments on both hosts, including Windows paths such as `C:/src/example`. [Phase 03 acceptance](receipts/phase-03-git-terminal-language-servers.md)

| Tool | Arguments | Result |
| --- | --- | --- |
| `git_discover_repository` | `rootId`, `relativePath` | The native worktree, its Git directory, and its common Git directory. |
| `git_select_repository` | `rootId`, `relativePath`, current workbench `context` | The selected worktree and advanced workbench context. Requires a write-enabled attachment. |
| `git_get_status` | None | The selected worktree's branch, HEAD, upstream and divergence when available, and entries with independent index and working-tree status. Rename and copy entries also give the original path. `trackedPaths` lists index paths within the captured scope. |
| `git_initialize_repository` | Absolute `destination`, current workbench `context` | The registered workspace `root` and `repository` in `data`. The IDE selects both after setup. Requires a write-enabled attachment. |
| `git_clone_repository` | Git `source`, absolute `destination`, current workbench `context` | The installed workspace `root` and selected `repository` in `data`. Requires a write-enabled attachment and a new or empty destination. |
| `git_get_diff` | Selected `worktreePath`, literal `path`, `layer` of `staged` or `working` | The Git patch and its layer, path, and repository identity. A selectable patch also returns its `preimage` and `hunks`. |
| `git_open_diff` | Selected `worktreePath`, literal `path`, `layer` of `staged` or `working`, current workbench `context` | The displayed tab’s view key, workspace, repository, path, layer, and current hunk selection metadata. Reuses the tab for an already-open worktree, path, and layer. Requires a write-enabled attachment. |
| `git_get_blame` | Selected `worktreePath`, literal `path`, optional full `commitId` and inclusive `firstLine` and `lastLine` | Returns the attributed lines and their originating commits and paths. The default reads the working file on disk. |
| `git_open_blame` | The blame arguments and current workbench `context` | Opens or refreshes a read-only blame tab for the same worktree, path, commit and range and returns its view key. |
| `git_close_blame` | The blame tab’s `viewKey` and current workbench `context` | Closes the tab in the attached workbench. |
| `git_close_diff` | The tab's `viewKey`, current workbench `context` | Closes the registered tab in the attached workbench. Requires a write-enabled attachment. |
| `git_stage_file` | Selected `worktreePath`, literal `path`, current workbench `context` | The path and repository with `staged: true`. Requires a write-enabled attachment. |
| `git_unstage_file` | Selected `worktreePath`, literal `path`, current workbench `context` | The path and repository with `staged: false`. Requires a write-enabled attachment. |
| `git_stage_hunks` | Selected `worktreePath`, literal `path`, `preimage` from a working diff, selected `hunkIds`, current workbench `context` | Stages only the selected hunks. Requires a write-enabled attachment. |
| `git_unstage_hunks` | Selected `worktreePath`, literal `path`, `preimage` from a staged diff, selected `hunkIds`, current workbench `context` | Unstages only the selected hunks and preserves working files. Requires a write-enabled attachment. |
| `git_get_commit_state` | Selected `worktreePath` | The branch, HEAD, current commit message, staged change availability, and commit `preimage`. Open the entire worktree root before committing its index. |
| `git_commit` | Selected `worktreePath`, commit `preimage`, `message`, current workbench `context` | Commits the reviewed index and returns the previous and new HEAD. Requires a write-enabled attachment. |
| `git_amend` | Selected `worktreePath`, commit `preimage`, `message`, current workbench `context` | Replaces the reviewed HEAD and returns the previous and new HEAD. Requires a write-enabled attachment. |
| `git_list_refs` | Selected `worktreePath` | Full ref names, object IDs, upstreams, peeled annotated tags, and linked worktrees. |
| `git_get_tag` | Selected `worktreePath`, short `tagName`, optional `expectedObjectId` | Returns the tag object ID, target object, tagger and annotation. |
| `git_create_tag` | Selected `worktreePath`, short `tagName`, full `targetCommit`, optional `message`, current workbench `context` | Creates a tag at the selected commit using native Git signing settings. |
| `git_delete_tag` | Selected `worktreePath`, short `tagName`, `expectedObjectId`, current workbench `context` | Deletes the ref only if it still points to the reviewed tag object. Use `objectId` from inspection, which is distinct from `targetObjectId` for an annotated tag. [Git update-ref](https://git-scm.com/docs/git-update-ref) |
| `git_list_worktrees` | Selected `worktreePath` | Native checkout paths, primary checkout, HEAD, branches, detached state, locks and prune reasons. |
| `git_add_worktree` | Selected `worktreePath`, absolute `destination`, full `targetCommit`, optional `newBranch` or `existingBranch`, current workbench `context` | Adds a linked checkout and returns its worktree row. Omitting both branch fields selects detached HEAD. |
| `git_open_worktree` | Selected `worktreePath`, reviewed `targetPath`, `expectedHead`, `expectedLocked`, optional full `expectedBranch`, current workbench `context` | Opens the reviewed checkout as an Explorer root and selects it for Git. |
| `git_lock_worktree`, `git_unlock_worktree`, `git_remove_worktree` | Same reviewed worktree fields and context as `git_open_worktree`. Lock also accepts a `reason`. | Changes the reviewed checkout’s lock or removes it after checking its working files and live editor buffers. Returns the affected worktree row. |
| `git_list_submodules` | Selected `worktreePath` | Lists indexed paths, recorded and checked-out commits, configuration and checkout state, local changes, and a review `revision` for each submodule. |
| `git_init_submodule`, `git_update_submodule`, `git_sync_submodule` | Selected `worktreePath`, exact submodule `path`, its `expectedRevision` from the list, current workbench `context` | Initializes the local configuration, checks out the recorded commit, or syncs the URL from `.gitmodules`. Mutations return the updated row. |
| `git_lfs_status` | Selected `worktreePath` | Returns native availability, filter configuration, configured remote names, and the committed file inventory with current checkout and cache state. |
| `git_lfs_fetch`, `git_lfs_checkout`, `git_lfs_pull` | Selected `worktreePath`, `expectedRevision` from LFS status, current workbench `context`. Fetch and pull also take a configured `remote`. | Downloads objects into the native cache, checks out cached working files, or performs both. Mutations return updated LFS status. |
| `git_terminal_open` | Selected full `worktreePath` and current workbench `context` | Opens a native shell document and returns its terminal ViewModel identity, checkout, and process state. |
| `git_terminal_list` | None | Lists terminal document identities and process state for the attached workbench. |
| `git_terminal_read` | Terminal `viewKey`, optional absolute `offset` and `maximumBytes` | Returns base64 terminal bytes, the next offset, and the earliest retained offset. The `truncated` flag marks an offset that fell behind the transcript. |
| `git_terminal_write`, `git_terminal_resize`, `git_terminal_close` | Terminal `viewKey` and current workbench `context`. Write takes `text`. Resize takes `columns` and `rows`. | Writes UTF-8 input verbatim, resizes the active PTY, or closes the terminal document and its shell. These tools require a mutating attachment. |
| `git_get_history` | Selected `worktreePath`, optional full `refName`, `anchor`, `offset`, and `limit` | Topologically sorted commits with parent IDs and refs. Reuse the returned `anchor` with the next offset to keep pages on the same history. |
| `git_get_commit_details` | Selected `worktreePath`, full `commitId` | Commit metadata, message, and file statuses compared with the first parent. An initial commit compares with the empty tree. |
| `git_open_history` | History arguments and current workbench `context` | Opens or reuses a Git History tab and returns its view key, page, and reference data. Requires a write-enabled attachment. |
| `git_close_history` | The history tab’s `viewKey`, current workbench `context` | Closes the tab in the attached workbench. Requires a write-enabled attachment. |
| `git_create_branch` | Selected `worktreePath`, local `branchName`, optional full `startCommit`, optional `switch`, current workbench `context` | Creates the branch at the reviewed commit or current HEAD. Switches only when requested. Requires a write-enabled attachment. |
| `git_switch_branch` | Selected `worktreePath`, local `branchName`, target `expectedHead`, current workbench `context` | Switches to the existing local branch. Use the branch object ID from `git_list_refs` as `expectedHead`. Requires a write-enabled attachment. |
| `git_rename_branch` | Selected `worktreePath`, local `branchName`, `expectedHead`, `newName`, current workbench `context` | Renames the reviewed local branch with its tracking configuration and reflog. Requires a write-enabled attachment. |
| `git_delete_branch` | Selected `worktreePath`, local `branchName`, `expectedHead`, current workbench `context` | Deletes a reviewed, fully merged local branch. Requires a write-enabled attachment. |
| `git_list_remotes` | Selected `worktreePath` | Remote names, displayed fetch and push URLs, and the configuration `revision`. URLs omit embedded credentials. |
| `git_add_remote` | Selected `worktreePath`, remote `name`, URL or local path in `value`, `revision`, current workbench `context` | Adds a native Git remote and returns the updated remote list. |
| `git_set_remote_url` | Selected `worktreePath`, remote `name`, URL or local path in `value`, optional `push`, `revision`, current workbench `context` | Updates the fetch URL, or the push URL when `push` is true. |
| `git_rename_remote` | Selected `worktreePath`, remote `name`, new name in `value`, `revision`, current workbench `context` | Renames the remote and its remote-tracking refs. |
| `git_remove_remote` | Selected `worktreePath`, remote `name`, `revision`, current workbench `context` | Removes the selected remote and its remote-tracking refs. |
| `git_fetch` | Selected `worktreePath`, remote `name`, `revision`, optional `prune`, current workbench `context` | Fetches tracking refs and returns the active branch’s upstream and divergence when available. |
| `git_push` | Selected `worktreePath`, remote `name`, configuration `revision`, local `branchName` and full `expectedHead`, `remoteBranch`, current workbench `context` | Pushes the reviewed local tip to the named branch. `forceWithLease: true` requires a full `expectedRemoteHead` and rejects stale remote tips. |
| `git_pull` | Selected `worktreePath`, remote `name`, configuration `revision`, checked-out `branchName` and full `expectedHead`, `remoteBranch`, current workbench `context`, optional `strategy` | Fetches and integrates the named branch with `fast-forward` (default), `merge`, or `rebase`. Reports divergence, local file protection, and paused conflict state. |
| `git_get_operation` | Selected `worktreePath` | Reads the active operation, unresolved literal paths, the pending commit when known, and the revision to review before recovery. `kind` is `none` if Git has no paused operation. |
| `git_start_operation` | Selected `worktreePath`, operation `kind`, full `expectedHead` and `targetCommit`, current workbench `context`, `expectedBranch` (full ref or null for detached HEAD), `mainline` when required | Runs `merge`, `rebase`, `cherry-pick`, or `revert` against the reviewed checkout. A cherry-pick or revert of a merge commit requires a one-based `mainline` parent. The returned operation can be reviewed and recovered if Git pauses. |
| `git_continue_operation` | Selected `worktreePath`, operation `revision`, current workbench `context` | Continues a merge, rebase, cherry-pick, or revert after conflict resolutions are staged. If Git pauses at another conflict, the response includes its updated state. |
| `git_abort_operation` | Selected `worktreePath`, operation `revision`, current workbench `context` | Aborts the reviewed native operation in that checkout and returns its resulting state. |
| `git_skip_operation` | Selected `worktreePath`, operation `revision`, current workbench `context` | Omits the pending commit when `canSkip` is true. Returns the new operation state, including the next pending commit and revision if Git pauses again. |
| `git_review_reset` | Selected `worktreePath`, full `targetCommit` | Returns the reviewed branch, HEAD, affected index and tracked paths, any untracked or ignored paths that block a hard reset, and a `revision`. |
| `git_reset` | Selected `worktreePath`, the reviewed `targetCommit` and `revision`, `mode`, current workbench `context` | Applies `soft`, `mixed`, or `hard` in the selected checkout. If `git_reset_changed` is returned, review the checkout again before retrying. [Git reset](https://git-scm.com/docs/git-reset) |
| `git_list_stashes` | Selected `worktreePath` | Lists stash selectors, commit IDs and messages with a checkout-bound stack revision. |
| `git_inspect_stash` | Selected `worktreePath`, reviewed `revision`, `selector`, and `objectId` | Returns the patch for the reviewed stash. |
| `git_create_stash` | Selected `worktreePath`, `message`, optional `includeUntracked`, current workbench `context` | Saves tracked changes and, when requested, untracked files. |
| `git_apply_stash`, `git_pop_stash`, `git_drop_stash` | Selected `worktreePath`, reviewed `revision`, `selector`, and `objectId`, current `context`, optional `restoreIndex` for apply and pop | Apply, apply and remove after success, or remove the reviewed entry. Conflict results include the affected paths and refreshed stack. [Git stash](https://git-scm.com/docs/git-stash) |
| `git_export_patch` | Selected `worktreePath`, `layer` | Returns `patchBase64` and its byte count for the selected `index` or `worktree` diff. |
| `git_review_patch` | Selected `worktreePath`, `layer`, `patchBase64` | Returns affected paths, `canApply`, and a review `revision` when applicable. |
| `git_apply_patch` | Selected `worktreePath`, `layer`, `patchBase64`, reviewed `revision`, current `context` | Applies the exact reviewed bytes to the chosen layer. On `git_patch_changed`, review again. [Git apply](https://git-scm.com/docs/git-apply) |
| `git_set_upstream` | Selected `worktreePath`, local `branchName`, `expectedHead`, optional full `upstream`, current workbench `context` | Sets tracking for the reviewed branch. Omit `upstream` to clear tracking. |

Selection is scoped to the attached workbench. Desktop commands and attached clients share the selection and status shown in Source Control and Project Explorer. On desktop restart, the workspace revalidates the saved checkout before issuing a new attachment. Select a worktree before reading status. If its selection changes while status is being prepared, refresh the context and read again. [Git integration](git-integration.md)

Pass the exact checkout-relative `path` returned by status, including spaces, Unicode, and other literal characters. If the index is locked or changed during preparation, the mutation returns `git_index_busy` or `git_index_changed`. Read the current status and retry after the other Git operation finishes.

Hunk IDs are local to one diff preimage. When a hunk action returns `git_diff_changed`, read or reopen the diff and choose IDs from the new result before retrying.

Commit and amend recheck the reviewed HEAD and index, then verify the proposed commit tree before Git publishes it. Restart the review when `git_commit_state_changed` is returned, including after a configured hook restages content. `git_commit_guard_unavailable` means Git cannot verify the reviewed tree before publication. On success, `indexPublished` indicates whether the actual index still matches the reviewed staged state.

History and ref queries require the full worktree root to be open. A nonzero history offset requires an anchor. The history page limit defaults to 80 and supports 1 through 500 commits per call.

Branch commands operate on the entire open worktree. A stale tip returns `git_branch_changed`. A branch checked out in another worktree returns `git_branch_in_use`. Save affected editor buffers if the operation returns `git_branch_editors_busy`.

Rename protects branches checked out in another worktree. Delete returns `git_branch_in_use` for every checked-out branch and `git_branch_unmerged` when its commits are not fully merged. Refresh references after either operation.

Remote configuration, fetch, push, and pull require write-enabled attachments. Refresh `git_list_remotes` when `git_remote_changed` is returned and `git_list_refs` when `git_branch_changed` is returned. Fetch accepts configured tracking and tag destinations to preserve local branch refs. [Git fetch](https://git-scm.com/docs/git-fetch) · [Git push](https://git-scm.com/docs/git-push) · [Git pull](https://git-scm.com/docs/git-pull)

Operation recovery uses the selected worktree root and a write-enabled attachment. Review `git_get_operation` again after stage, after an external Git change, or when recovery returns `git_operation_changed`. Recovery rejects unsaved editor buffers in the selected checkout. [Git rebase](https://git-scm.com/docs/git-rebase) · [Git cherry-pick](https://git-scm.com/docs/git-cherry-pick)

## One feature registry

Register each IDE command and query with a stable ID, typed input and output, context, and required capabilities. The workbench binds its commands to these registrations. The MCP host describes the same operations with stable tool names and JSON Schemas for `tools/list`, then maps `tools/call` requests to the registered command or query. UI gestures such as docking have semantic tool operations such as dock, float, and focus. A coverage check compares the complete feature registry to the MCP tool registry and fails when a supported operation has no tool. [MCP tool protocol](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)

MCP calls enter the main MVC controller's typed dispatch entry point and run through the CQRS Unit of Work. They carry the authenticated client and captured workspace, document, emulator instance, or workbench view identity as appropriate. Calls originating in Avalonia continue to use the `IView` that registered with its controller. An MCP call returns its result to the requesting client. Model updates refresh attached views on the Avalonia UI thread.

## ViewModels as public context contracts

The IDE ViewModels are the public source for MCP tool context. Each tool declares the ViewModel contract it uses. Its context schema is generated from the public members that the ViewModel explicitly exposes to MCP. Return those members as a read-only snapshot of the ViewModel's current state, using the same field names and meanings in discovery results and tool calls. Command arguments and results keep their typed CQRS contracts.

For example, an editor tool uses the registered editor ViewModel's workspace ID, document ID, `ViewKey`, and document revision. The agent reads the context, then sends those fields to identify the document and its expected revision when applying edits. The host resolves them against the live registration and authorized workspace before dispatch. A closed view, stale revision, or mismatched target produces a structured result. Context fields describe or select state. Commands apply changes through their existing handlers.

Mark MCP-visible members on the ViewModels and generate their schemas and serialization from that metadata. Capture view-bound state on the Avalonia UI thread. Limit the wire context to those declared members so callbacks, commands, service references, and credentials stay internal. Test schema parity between the ViewModel and tool discovery whenever a public context changes.

## Tool coverage

The names below illustrate discoverable tools. Each area includes read and mutation operations wherever the IDE supports them.

| IDE feature | MCP tool coverage |
| --- | --- |
| Workbench and localization | List command availability, inspect and change view focus and docking, manage layouts, open tool windows, and get or set language and preferences. |
| Workspaces and Project Explorer | Open projects and folders, choose workspaces and roots, list children, open files, and reveal documents. |
| Source editor and language servers | Inspect open buffers and revisions, open, edit, save and close documents, manage language server settings and lifecycle, and request completions, symbols, navigation, diagnostics, formatting and refactors. |
| Git and terminal | Inspect repositories, worktrees, status, diffs and history. Expose the same staging, branch, commit, remote, conflict and other Git operations. Open, read, write, and close a terminal for the authorized workspace. |
| Build, run and tests | Discover configurations, build, launch or attach, and run tests. Return diagnostic locations, test outcomes, and failure artifacts. |
| Emulators and adapters | Manage bucket and installed-bundle lifecycle, select presets and versions, list sessions and capabilities, and control supported emulator lifecycle operations. |
| Debugging and guest inspection | Read registers and memory, set supported breakpoints and watchpoints, inspect disassembly and trace, step execution, and expose optional guest services. |
| Video, audio, input and replay | Capture the display, read supported stream chunks, inspect playback, set host volume, route scoped input, and seek eligible recordings. |
| Media, snapshots and diagnostics | List, mount, and eject media, manage supported snapshots, and read console, logs, and diagnostic artifacts. |
| Agents and operations | List, launch, and attach configured agents where supported. Inspect permission or operation outcomes and query, wait for, or cancel running operations. |

Backend capabilities, workbench state, and client grants govern actual tool availability. Return a structured unavailable result when a required capability or context is absent. Discovery and capability changes use the SDK's supported MCP notifications where negotiated.

## Hosting and results

- **Attach.** A lightweight stdio entry point attaches to the selected running IDE instance for CLI clients. Desktop clients can attach to a loopback Streamable HTTP endpoint on the same instance. Both transports share the tool registry and dispatch service. The HTTP host validates the client, token, and Origin as applicable. [MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- **Scope.** Bind every client to its authorized IDE instance, workspace and capabilities. Validate arguments, resolve paths and IDs in that context, and apply command eligibility before dispatch. The configured adapter or emulator host enforces its own authorization. Audit tool name, actor, context, operation ID, and outcome.
- **Return.** Provide structured results with a concise summary, contextual IDs, and stable errors. Report revisions when relevant. Use explicit expected revisions or context on mutations that depend on prior state. Carry request cancellation through the dispatcher and reuse the same operation identity for retries where the feature supports it.
- **Long-running work.** Report progress for clients that request it. A build, launch, Git operation, or bundle install can return an operation ID with status, result, and cancel tools. Clients that negotiate MCP Tasks can use the SDK extension for task status and result retrieval.
- **Large results.** Read logs, disassembly, or supported media streams through bounded tools with cursor or stream handles. Image captures can return MCP image content. Resources can provide alternate access to the same data.

### Background operation handles

Git clone, fetch, pull, push, LFS fetch, and LFS fetch-and-checkout accept an optional `background: true` argument. The start response uses the accepted operation ID in both the envelope and `data.operationId`. The data also identifies the tool, current state, and available timestamps. Calls with the default `background: false` wait for their typed result.

| Tool | Input | Result |
| --- | --- | --- |
| `workbench_operation_status` | `operationId` | Returns the current state, cancellation availability, and timestamps. States are `queued`, `running`, `cancelling`, `succeeded`, `failed`, and `cancelled`. |
| `workbench_operation_result` | `operationId` | Returns the original operation envelope in `data` after completion. A pending operation returns `operation_pending`. |
| `workbench_operation_cancel` | `operationId` and current workbench `context` | Requests cancellation and returns the resulting status. An operation that already finished keeps its original result. |

Use the same attachment credential to follow a handle over HTTP or stdio. The workbench scopes handles to that attachment, cancels its running work when the grant is revoked, and releases retained handles when the view closes. Each workbench admits up to eight pending operations and keeps up to 64 handles. Finished results are eligible for eviction after 15 minutes or when capacity is needed.

Verify tool discovery and invocation through both transports. Compare UI and MCP outcomes for the same typed operation. Verify ViewModel context schema parity, live context resolution, scoped authorization, ambiguous or stale targets, capability changes, progress and cancellation, and every registered feature area. Track this feature under `FR-RETRO-015`, `TR-RETRO-MCP-015`, and `TEST-RETRO-015`.
