# Functional Requirements (QBrain.AI)

## FR-RETRO-001 Dockable cross-platform IDE shell

Operate and debug emulator sessions from a common dockable desktop on Windows and Linux.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Dock, tab, drag out into a separate window, and redock a supported tool pane on each target host.
- [ ] Save and restore a layout across desktop restarts while the emulator session remains independently accessible.
- [ ] Open supported Emulation, console, logs, debugger, snapshot, test, and agent activity views scoped to the selected instance.

## FR-RETRO-002 Target selection and project launch

Select a target preset, build the selected project, and launch or attach to the emulator from the IDE.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] The same IDE offers UAE, VICE, and ViceSharp presets and accepts an additional adapter without converting the built-in presets into special cases.
- [ ] A failed build exposes a source diagnostic in the IDE and starts no emulator. A successful debug launch opens Emulation for the selected target.
- [ ] Display the selected instance and lifecycle. Surface isolated faults, in-progress operations, stale revisions, and loss of control without applying an unauthorized mutation.

## FR-RETRO-003 Emulation video, audio, input, and replay controls

Use the Emulation document for the selected instance, including host audio level, captured frames, input, and playback of supported recordings.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Render and capture a known fixture frame for the selected instance.
- [ ] Play selected guest audio and route keyboard and pointer input to the selected guest according to the adapter’s capabilities.
- [ ] Keep controls responsive under video and audio load and display frame-delivery and audio-underrun observations where available.
- [ ] In a seekable recording, drag the playhead on the progress bar under the time marker and update the displayed timestamp and frame. Set volume independently. For live emulation show elapsed time and disable unavailable seeking.

## FR-RETRO-004 Debugger, console, and guest inspection tools

Use the same tool window system to inspect CPU state, memory, disassembly, breakpoints, events, console, source symbols, and optional guest-specific tools.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Pause, step, and continue through a fixture and show the current location, registers, memory, and breakpoint state for the selected target.
- [ ] Resolve symbols and source when valid for the selected build. On missing or mismatched symbols retain raw address and disassembly views.
- [ ] Expose supported console input, early boot output, guest logs, and host events with distinct sources.
- [ ] Expose guest-specific inspectors when advertised and describe unavailable capabilities while keeping supported machine-level views usable.

## FR-RETRO-005 Media, snapshots, tests, and diagnostics

Use supported media, checkpoint, test, and failure-evidence operations from the shared IDE.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Show attached media for the selected instance and prompt before a destructive operation where policy requires it.
- [ ] Show available snapshots, compatibility, compare and restore where supported. Never offer a failed or incomplete snapshot for restore.
- [ ] Run a supported test from the IDE, show assertions and failure details, and open exported diagnostic evidence or a supported replay without losing the session context.

## FR-RETRO-006 Integrated agent activity and scoped control

Launch or attach a configured CLI agent and inspect its scoped emulator activity from the desktop.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] From the desktop, connect a configured agent and display reads, authorized actions, and denials for the selected instance.
- [ ] On agent or service disconnection, explain the failure and offer reattachment when available. Agent loss does not end a running emulator session.

## FR-RETRO-007 IDE menus and command bars

Use menus and command bars to invoke the same predefined IDE actions with consistent availability and outcomes.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Equivalent menu and command-bar entries resolve to the same predefined command and invoke the same operation for the same workspace context.
- [ ] When the active target, instance, or operation state changes, both surfaces show the resulting command availability. An unavailable invocation produces no operation.
- [ ] Build Project runs only the build. Start Debugging builds and launches only on success. Both surfaces display progress and source diagnostics from the same model.
- [ ] A UI command delivers its result on the Avalonia UI thread to the registered view that raised the request, even when focus or docking has changed. A closed or replaced view receives no callback.
- [ ] In the tabbed document editor, document commands use the active editor tab. Switching tabs after invoking Save keeps the captured document and originating editor as the pending request target and result recipient.
- [ ] While a background operation logically blocks interaction, the affected UI shows the platform wait cursor. Available controls remain responsive and keep their normal cursor. The normal cursor returns when the interaction block clears.

## FR-RETRO-008 Command presentation and IDE localization

Show each registered IDE command with its icon, localized label, and default keyboard shortcut. Resolve IDE-owned strings and images from stable resource slugs.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] A command's label, icon, and displayed default shortcut come from the same descriptor in the menu and command bar. The keyboard binding invokes the same eligible command in its activation scope.
- [ ] Selecting the IDE display language refreshes strings, images, accessible names, and layout direction in open windows and persists the selection. Command actions and default bindings remain stable across locales.
- [ ] A partially translated pack uses the first available text or image per slug from the selected locale, parent locales, and bundled English, in that order.

## FR-RETRO-009 Manage emulator bundles from Git buckets

Manage emulator executables, adapter bindings, and runtime files as versioned bundles in the Emulators manager. Discover releases from Scoop-style Git bucket repositories.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Emulators refreshes configured Git buckets, displays compatible releases and installed versions, and offers install, update, rollback, and remove operations.
- [ ] A project selects a bucket, bundle, version, and manifest revision. Catalog refresh and installing another release preserve project pins and running sessions. Rollback selects a previously verified version.
- [ ] On network or installation failure the last valid catalog and verified installed versions remain usable. A project launches its installed version offline.

## FR-RETRO-010 Use one emulator session contract across backends

Support UAE, VICE, ViceSharp, and future emulators through a shared launch, attach, and capability-driven session interface.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] A compatible adapter opens or attaches a session, reports its identity, state, and capabilities, and releases the session connection on close.
- [ ] The IDE enables only operations advertised by the selected session, adapts to capability changes, and presents unsupported operations and failures consistently.
- [ ] An adapter may add optional, versioned capabilities for future machine families or workflows while base session features remain available to existing clients.

## FR-RETRO-011 Tabbed source editor

Edit project source using the Avalonia-based document workspace, with navigation from diagnostics and debugger source locations.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Open and edit project files in separate tabs with independent buffers, undo history, caret, and selection. Preserve this state when switching and floating tabs.
- [ ] Save the captured document revision. Later edits remain dirty after an older save completes. Failed saves preserve the buffer and report the error to the originating view.
- [ ] Go to the project file, line, and column from build diagnostics or debugger source location, and show the reported state of breakpoint requests from the editor gutter.

## FR-RETRO-012 Explore workspace projects and folders

Use a dockable Project Explorer to browse a workspace containing project and folder roots and navigate between the tree and the editor.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Open a project or folder and browse project and folder roots together in one expandable tree. A plain folder opens for editing without requiring project metadata.
- [ ] Open a selected file by keyboard or pointer, activate its existing tab on repeat open, and reveal an open document in the tree. Same-name files under different roots open their correct documents.
- [ ] Restore workspace roots, expanded paths, and selection when reopening. Refresh externally changed branches and display unavailable roots while other roots remain usable.

## FR-RETRO-013 Complete workspace Git integration

Use Git from the Source Control pane, Project Explorer, editor, and integrated terminal across workspace repositories and worktrees.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Discover or open repositories, initialize or clone a workspace root, select the repository and worktree visibly, and decorate the project tree with current Git status.
- [ ] Review staged and unstaged diffs, stage and unstage files or hunks, commit or amend, inspect history and blame, and manage branches, tags, upstream tracking, fetch, pull, and push.
- [ ] Support stash and patch workflows, merge, rebase, cherry-pick, revert and reset. Detect conflicts and ongoing operations, edit and stage resolutions, and expose continue, skip, and abort where applicable.
- [ ] Manage linked worktrees and submodules. Honor Git LFS configuration where installed and allow advanced Git commands in the repository-scoped integrated terminal.

## FR-RETRO-014 Language server support

Use standard LSP servers in AvaloniaEdit for editing assistance, navigation, diagnostics, and refactoring across workspace roots.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Configure and select installed language servers by language and workspace root. Enable supported features after the server initializes.
- [ ] Use available completion, hover, symbols, references, definition navigation, diagnostics, formatting, rename, and code actions from the editor and Problems view.
- [ ] Keep source editing available if a language server is missing or fails. Show server status and allow restart and resynchronization of open buffers.

## FR-RETRO-015 Expose all Retro IDE features as MCP tools

Use the official .NET MCP SDK and Microsoft hosting extensions to expose every supported IDE feature through MCP, including workbench operations.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Every supported IDE feature operation has a discoverable MCP tool with stable identity and typed parameters, including the workbench, editor, language servers, Git, terminal, build and test, emulators, provider-dependent debugging, media, and agent operations.
- [ ] CLI and desktop agent clients attach to the same selected IDE instance using stdio and loopback Streamable HTTP. Calls operate in their granted workspace and capability context.
- [ ] A supported tool produces the same modeled effect and domain result as its desktop feature. The client gets a structured result and affected workbench state is updated.
- [ ] Long-running features expose progress, status, result, and cancellation. Large results provide readable chunks or supported MCP media content.
- [ ] An MCP client can discover and read the public context of a registered IDE ViewModel and reuse it to address the same workspace, document, or view in a tool invocation.

