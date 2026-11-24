# Retro IDE C4 model

[workspace.dsl](workspace.dsl) is the current Structurizr model. [Design snapshot `design-07-source-editor`](versions/design-07-source-editor/manifest.json) adds the AvaloniaEdit source control and its diagnostic navigation workflow. Earlier snapshots record [emulator integration](versions/design-06-emulator-integration/manifest.json), the [system wait cursor](versions/design-05-system-wait-cursor/manifest.json), [controller ownership and tabbed editing](versions/design-04-controller-ownership/manifest.json), [MVVM Toolkit and view events](versions/design-03-mvvm-view-events/manifest.json), [command localization](versions/design-02-command-localization/manifest.json), [SimpleMVC and CQRS](versions/design-01-mvc-commanding/manifest.json), and [the IDE migration](versions/design-00-ide-split/manifest.json). The project currently has wireframes, storyboards, and target presets. Components tagged `Planned` describe implementation work.

| View | What it shows |
| --- | --- |
| [System context](versions/design-07-source-editor/exports/mermaid/structurizr-SystemContext.mmd) | Developers, build tools, agents, QBrain, emulator backends, and bucket and artifact sources |
| [Containers](versions/design-07-source-editor/exports/mermaid/structurizr-Containers.mmd) | Desktop responsibilities, workspace settings, and local emulator bundle storage |
| [Desktop components](versions/design-07-source-editor/exports/mermaid/structurizr-DesktopComponents.mmd) | Workbench, source editor, controllers, emulator management, and session coordination |
| [Adapter components](versions/design-07-source-editor/exports/mermaid/structurizr-AdapterComponents.mmd) | Resolution of pinned bundle versions and the three backend boundaries |
| [Emulator bundle install](versions/design-07-source-editor/exports/mermaid/structurizr-EmulatorBundleInstall.mmd) | Select, verify, and install a bucket release, then resolve a project's pinned entry points |
| [Emulator session contract](versions/design-07-source-editor/exports/mermaid/structurizr-EmulatorSessionContract.mmd) | Negotiate a session, use typed capability ports, and receive separate control and media streams |
| [Command dispatch](versions/design-07-source-editor/exports/mermaid/structurizr-CommandDispatch.mmd) | Workbench command through its registered owner, CQRS, and result delivery on the Avalonia UI thread |
| [Tabbed document save](versions/design-07-source-editor/exports/mermaid/structurizr-DocumentMdiSave.mmd) | An editor tab registers with the document controller, which owns save dispatch and result delivery |
| [Source editor workflow](versions/design-07-source-editor/exports/mermaid/structurizr-SourceEditorWorkflow.mmd) | Open a diagnostic's source location in the AvaloniaEdit tab without losing its document state |
| [System wait cursor](versions/design-07-source-editor/exports/mermaid/structurizr-SystemWaitCursor.mmd) | The workbench applies and clears the native wait cursor based on interaction blocks |
| [Command localization](versions/design-07-source-editor/exports/mermaid/structurizr-CommandLocalization.mmd) | Display language selection, preference persistence, and typed slug resolution for text and icons |
| [Build, launch, and debug](versions/design-07-source-editor/exports/mermaid/structurizr-BuildLaunchDebug.mmd) | Build-before-launch workflow through the controller and Unit of Work, using UAE as an example |
| [Windows deployment](versions/design-07-source-editor/exports/mermaid/structurizr-WindowsDeployment.mmd) | Planned Windows developer workstation with selectable local targets |
| [Linux deployment](versions/design-07-source-editor/exports/mermaid/structurizr-LinuxDeployment.mmd) | Planned Linux developer workstation with selectable local targets |

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
