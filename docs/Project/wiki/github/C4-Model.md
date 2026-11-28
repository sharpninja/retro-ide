# Retro IDE C4 model

[workspace.dsl](workspace.dsl) is the current Structurizr model. [Design snapshot `design-12-mcp-viewmodel-context`](versions/design-12-mcp-viewmodel-context/manifest.json) defines ViewModels as public MCP context contracts. Earlier snapshots record [the IDE MCP host](versions/design-11-ide-mcp-tools/manifest.json), [language servers](versions/design-10-language-servers/manifest.json), [Git integration](versions/design-09-git-integration/manifest.json), [Project Explorer](versions/design-08-workspace-explorer/manifest.json), the [source editor](versions/design-07-source-editor/manifest.json), [emulator integration](versions/design-06-emulator-integration/manifest.json), the [system wait cursor](versions/design-05-system-wait-cursor/manifest.json), [controller ownership and tabbed editing](versions/design-04-controller-ownership/manifest.json), [MVVM Toolkit and view events](versions/design-03-mvvm-view-events/manifest.json), [command localization](versions/design-02-command-localization/manifest.json), [SimpleMVC and CQRS](versions/design-01-mvc-commanding/manifest.json), and [the IDE migration](versions/design-00-ide-split/manifest.json). The [accepted Phase 01 snapshot](versions/implementation-01-workbench-mcp/manifest.json) records the implemented workbench and scoped MCP focus. The [candidate snapshot](versions/implementation-01-workbench-mcp-candidate/manifest.json) preserves the prior checkpoint. Components tagged `Planned` describe later work.

The [accepted Phase 02 snapshot](versions/implementation-02-workspace-editor-localization/manifest.json) records Project Explorer, independent AvaloniaEdit tabs, live localization, and scoped MCP operations. It carries forward the [package debugger design](versions/design-13-package-debugger/manifest.json), which traces verified package profiles and the live machine description to CPU-specific views. [Phase 02 verification](../../receipts/phase-02-workbench-explorer-editor.md) · [Debugger storyboard STB17](../../design/_manifest.md#stb17-package-debugger)

The [accepted Phase 03 snapshot](versions/implementation-03-git-terminal-language-servers/manifest.json) records native Git and repository terminals, language-server document synchronization, and scoped background operation handles across HTTP and stdio. The shared workspace gate coordinates editor and Git publication. Queued Git writes recheck checkout identity before execution. [Phase 03 verification](../../receipts/phase-03-git-terminal-language-servers.md)

The [accepted Phase 04 snapshot](versions/implementation-04-emulator-manager-adapter-conformance/manifest.json) records verified package installation and project pins, shared Emulators UI and MCP commands, and fixture conformance for pinned sessions, subscription cleanup, machine descriptors, and map changes. Phase 05 integrates shipped providers and Phase 06 consumes the descriptor and profile contracts in the debugger panes. [Phase 04 verification](../../receipts/phase-04-emulator-manager-adapter-conformance.md)

| View | What it shows |
| --- | --- |
| [System context](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-SystemContext.mmd) | Developers, native tools, agents, QBrain, package sources, and planned shipped emulator integrations |
| [Containers](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-Containers.mmd) | Desktop, MCP stdio bridge, workspace files and settings, and verified local emulator bundle storage |
| [Desktop components](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-DesktopComponents.mmd) | Workbench, source tools, native Git, language services, MCP, and package and session components |
| [Phase 03 workflow components](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-Phase03WorkflowComponents.mmd) | Git, the workspace write gate, editor publication, terminals, language servers, and background operation tracking |
| [Phase 04 workflow components](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-Phase04WorkflowComponents.mmd) | Package management, typed session control, subscriptions, descriptor and profile validation, and the conformance fixture |
| [Emulator bundle install](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-EmulatorBundleInstall.mmd) | Verify and install a compatible release, then persist its project pin and rollback history |
| [Emulator package parity](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-EmulatorPackageParity.mmd) | Route UI, HTTP, and stdio through shared CQRS and refresh the current project in the registered pane |
| [Pinned emulator session lifecycle](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-PinnedEmulatorSessionLifecycle.mmd) | Lease the fixture binding, drain subscriptions, and release the lease after provider disposal |
| [Machine descriptor and profile](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-MachineDescriptorAndProfile.mmd) | Verify the fixture's descriptor and installed profile, then scope mapped access to the current map revision |
| [Adapter components](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-AdapterComponents.mmd) | Shared pinned bundle and session contracts with planned Phase 05 provider integrations |
| [Emulator session contract](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-EmulatorSessionContract.mmd) | Planned ViceSharp integration in Phase 05 and player consumption in Phase 07 |
| [Package and debugger components](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-PackageDebuggerComponents.mmd) | Accepted package and machine contracts with planned Phase 06 debugger presentation |
| [Debugger machine description](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-DebuggerMachineDescription.mmd) | Planned shipped descriptor and selected debugger target integration in Phases 05 and 06 |
| [Debugger memory display](versions/implementation-04-emulator-manager-adapter-conformance/exports/mermaid/structurizr-DebuggerMemoryDisplay.mmd) | Planned Phase 06 memory pane with bank, revision, and saved byte-order display |
| [Command dispatch](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-CommandDispatch.mmd) | Workbench command through its registered owner, CQRS, and result delivery on the Avalonia UI thread |
| [Workbench focus through UI and MCP](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-WorkbenchFocusParity.mmd) | Toolbar, menu, shortcut, stdio and HTTP use one typed focus operation |
| [MCP tool dispatch](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-McpToolDispatch.mmd) | Read the public editor ViewModel context, dispatch the scoped operation through CQRS, and return the result to the calling agent |
| [Tabbed document save](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-DocumentMdiSave.mmd) | Stage source text, wait for publication, and return the save outcome to its original tab |
| [Source editor workflow](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-SourceEditorWorkflow.mmd) | Open a diagnostic's source location in the AvaloniaEdit tab |
| [Language server completion](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-LanguageServerCompletion.mmd) | Synchronize an unsaved buffer, request completion, and return results to the originating editor version |
| [Language server document synchronization](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-LanguageServerDocumentSync.mmd) | Send the committed save after document synchronization and before a subsequent close |
| [Workspace Explorer opening](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-WorkspaceExplorerOpen.mmd) | Browse project and folder roots and open or activate the selected file's editor tab |
| [Workspace Explorer refresh](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-WorkspaceExplorerRefresh.mmd) | Restore roots, expansion and selection, then refresh affected expanded directories after external changes |
| [Git stage, commit, and push](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-GitStageCommitPush.mmd) | Validate the selected checkout across separate staging, commit, and push actions |
| [Git checkout mutation](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-GitCheckoutMutation.mmd) | Reject a queued write when the selected checkout resolves to changed Git metadata |
| [Repository terminal](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-RepositoryTerminalSession.mmd) | Start an interactive shell in the verified worktree and return bounded output |
| [MCP operation lifecycle](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-McpOperationLifecycle.mmd) | Start background work, check or cancel it, and read the outcome through the same grant across transports |
| [System wait cursor](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-SystemWaitCursor.mmd) | The workbench applies and clears the native wait cursor based on interaction blocks |
| [Command localization](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-CommandLocalization.mmd) | Display language selection, preference persistence, and typed slug resolution for text and icons |
| [Build, launch, and debug](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-BuildLaunchDebug.mmd) | Build-before-launch workflow through the controller and Unit of Work, using UAE as an example |
| [Windows deployment](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-WindowsDeployment.mmd) | Verified Windows workbench and native tools with planned emulator targets |
| [Linux deployment](versions/implementation-03-git-terminal-language-servers/exports/mermaid/structurizr-LinuxDeployment.mmd) | Verified Linux workbench and native tools with planned emulator targets |

## Migrated design

The former UAE Developer `Avalonia Desktop` container is now the Retro IDE Desktop. The former pane list is represented as workbench and docking, Emulation and replay, debugger and guest inspectors, media, tests and diagnostics, and agent session and activity. The launch workflow and adapter catalog generalize the former UAE-only desktop client link. The model links the new components to the [Retro IDE requirement ownership](../../requirement-ownership.md) and [storyboard manifest](../../design/_manifest.md).

Retro IDE owns the desktop workflow, bundle management, and the [shared emulator adapter contract](../../emulator-adapter-contract.md). UAE Developer owns its UAE40 profile, native worker and IPC, control service, backend MCP boundary, and supporting evidence. The UAE desktop-facing bridge is pending. VICE and ViceSharp are external systems with independent adapter capabilities. The workstation views show all three choices. A project selects an installed bundle version and target preset.

## Keeping the model current

Revise the live DSL and validate it with the same local Structurizr CLI used for the baseline. Export a new version directory with its manifest when the design reaches a new checkpoint. Keep earlier snapshots and their exports intact.

For local validation from this directory with the existing CLI image:

```powershell
docker run --rm --pull=never --network none -v "${PWD}:/workspace:ro" structurizr/cli:2025.11.09 validate -workspace /workspace/workspace.dsl
```

See the [Structurizr DSL reference](https://docs.structurizr.com/dsl/language) and [Mermaid export documentation](https://docs.structurizr.com/export/mermaid).
