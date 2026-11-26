# Retro IDE C4 model

[workspace.dsl](workspace.dsl) is the current Structurizr model. [Design snapshot `design-12-mcp-viewmodel-context`](versions/design-12-mcp-viewmodel-context/manifest.json) defines ViewModels as public MCP context contracts. Earlier snapshots record [the IDE MCP host](versions/design-11-ide-mcp-tools/manifest.json), [language servers](versions/design-10-language-servers/manifest.json), [Git integration](versions/design-09-git-integration/manifest.json), [Project Explorer](versions/design-08-workspace-explorer/manifest.json), the [source editor](versions/design-07-source-editor/manifest.json), [emulator integration](versions/design-06-emulator-integration/manifest.json), the [system wait cursor](versions/design-05-system-wait-cursor/manifest.json), [controller ownership and tabbed editing](versions/design-04-controller-ownership/manifest.json), [MVVM Toolkit and view events](versions/design-03-mvvm-view-events/manifest.json), [command localization](versions/design-02-command-localization/manifest.json), [SimpleMVC and CQRS](versions/design-01-mvc-commanding/manifest.json), and [the IDE migration](versions/design-00-ide-split/manifest.json). The [accepted Phase 01 snapshot](versions/implementation-01-workbench-mcp/manifest.json) records the implemented workbench and scoped MCP focus. The [candidate snapshot](versions/implementation-01-workbench-mcp-candidate/manifest.json) preserves the prior checkpoint. Components tagged `Planned` describe later work.

The [accepted Phase 02 snapshot](versions/implementation-02-workspace-editor-localization/manifest.json) records Project Explorer, independent AvaloniaEdit tabs, live localization, and scoped MCP operations. It carries forward the [package debugger design](versions/design-13-package-debugger/manifest.json), which traces verified package profiles and the live machine description to CPU-specific views. [Phase 02 verification](../../receipts/phase-02-workbench-explorer-editor.md) · [Debugger storyboard STB17](../../design/_manifest.md#stb17-package-debugger)

| View | What it shows |
| --- | --- |
| [System context](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-SystemContext.mmd) | Developers, build tools, agents, QBrain, emulator backends, and bucket and artifact sources |
| [Containers](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-Containers.mmd) | Desktop, MCP stdio bridge, workspace content and settings, and local emulator bundle storage |
| [Desktop components](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-DesktopComponents.mmd) | Workbench, Project Explorer, source editor, Language Services, Source Control, emulator management, and the IDE MCP host |
| [Adapter components](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-AdapterComponents.mmd) | Resolution of pinned bundle versions and the three backend boundaries |
| [Emulator bundle install](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-EmulatorBundleInstall.mmd) | Select, verify, and install a bucket release, then resolve a project's pinned entry points |
| [Emulator session contract](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-EmulatorSessionContract.mmd) | Negotiate a session, use typed capability ports, and receive separate control and media streams |
| [Package and debugger components](versions/design-13-package-debugger/exports/mermaid/structurizr-PackageDebuggerComponents.mmd) | Installed profile data, adapter negotiation, the debugger target model, CPU references, and the workbench |
| [Debugger machine description](versions/design-13-package-debugger/exports/mermaid/structurizr-DebuggerMachineDescription.mmd) | Bind a descriptor and map revision from the selected live session to its verified platform profile |
| [Debugger memory display](versions/design-13-package-debugger/exports/mermaid/structurizr-DebuggerMemoryDisplay.mmd) | Scope reads by CPU, space, bank and revision, and apply the user's saved display byte order |
| [Command dispatch](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-CommandDispatch.mmd) | Workbench command through its registered owner, CQRS, and result delivery on the Avalonia UI thread |
| [Workbench focus through UI and MCP](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-WorkbenchFocusParity.mmd) | Toolbar, menu, shortcut, stdio and HTTP use one typed focus operation |
| [MCP tool dispatch](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-McpToolDispatch.mmd) | Read the public editor ViewModel context, dispatch the scoped operation through CQRS, and return the result to the calling agent |
| [Tabbed document save](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-DocumentMdiSave.mmd) | An editor tab registers with the document controller, which owns save dispatch and result delivery |
| [Source editor workflow](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-SourceEditorWorkflow.mmd) | Open a diagnostic's source location in the AvaloniaEdit tab |
| [Language server completion](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-LanguageServerCompletion.mmd) | Synchronize an unsaved document, request completion, and return results to the originating editor version |
| [Workspace Explorer opening](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-WorkspaceExplorerOpen.mmd) | Browse project and folder roots and open or activate the selected file's editor tab |
| [Workspace Explorer refresh](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-WorkspaceExplorerRefresh.mmd) | Restore roots, expansion and selection, then refresh affected expanded directories after external changes |
| [Git stage, commit, and push](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-GitStageCommitPush.mmd) | Run separate staging, commit, and push actions in the selected worktree using native Git |
| [System wait cursor](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-SystemWaitCursor.mmd) | The workbench applies and clears the native wait cursor based on interaction blocks |
| [Command localization](versions/implementation-02-workspace-editor-localization/exports/mermaid/structurizr-CommandLocalization.mmd) | Display language selection, preference persistence, and typed slug resolution for text and icons |
| [Build, launch, and debug](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-BuildLaunchDebug.mmd) | Build-before-launch workflow through the controller and Unit of Work, using UAE as an example |
| [Windows deployment](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-WindowsDeployment.mmd) | Planned Windows developer workstation with selectable local targets |
| [Linux deployment](versions/implementation-01-workbench-mcp/exports/mermaid/structurizr-LinuxDeployment.mmd) | Planned Linux developer workstation with selectable local targets |

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
