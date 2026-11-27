# PLAN-RETROIDE-001 — Retro IDE BDPv4 implementation plan

**Status.** Phases 01 and 02 were accepted on 2026-11-26. Phase 03 was accepted on 2026-11-27. Phase 04 is next. [Phase 01 verification](../receipts/phase-01-workbench-mcp.md) · [Phase 02 verification](../receipts/phase-02-workbench-explorer-editor.md) · [Phase 03 verification](../receipts/phase-03-git-terminal-language-servers.md)

**Baseline.** The original Retro IDE QBrain baseline has 15 functional, 15 technical, and 15 testing records across 15 mappings and 176 acceptance criteria. The current requirements also include the debugger CPU and emulator machine description groups in the traceability table below. Published source `e066c7325f3ad3c9a14c1310fbeba0c8b531edda` and wiki `13fc5aa1f0729e0879e9e515824ce2d9d56d4cb7` document local validation. [Published architecture](https://github.com/sharpninja/retro-ide/wiki/Architecture) · [MCP Tools](https://github.com/sharpninja/retro-ide/wiki/MCP-Tools) · [Accepted Phase 01 C4 model](../architecture/structurizr/versions/implementation-01-workbench-mcp/manifest.json)

## Goal and V²

- **Viable.** A real Windows and Linux workbench can exercise live ViewModel registration and both MCP transports with a local fixture. This proves shared dispatch and context behavior before installing an emulator. Later phases prove the versioned adapter contract against each shipped backend.
- **Valuable.** A developer can inspect and control the same docked IDE from the workbench, a CLI agent, or a desktop agent. Subsequent phases add project navigation, editing, Git, language servers, builds, and debugging for UAE, VICE, and ViceSharp.
- **Release boundary.** Deliver Retro IDE, its emulator bundle manager and MCP tools on Windows and Linux. Backend authorization and emulation behavior belong to the selected adapter host. The UAE worker and A1200 OS have their own workspaces. [Requirement ownership](../requirement-ownership.md) · [Published FR → TR → TEST mapping](https://github.com/sharpninja/retro-ide/wiki/TR-per-FR-Mapping)

## Architecture and build seams

| Planned project | Responsibility |
| --- | --- |
| `RetroIde.Application` | Feature registry, typed command/query contracts, CQRS Unit of Work, application model and ports. Queries read state. Commands validate context and publish outcomes. |
| `RetroIde.Workbench` | Avalonia and Dock.Avalonia, ViewModels and controllers, composition, window/MDI routing, persisted layout, and dispatcher adapter. |
| `RetroIde.Mcp` | Official MCP C# SDK tool discovery, schemas, scope, transport policy, operation tracking, and request/response mapping. Resolve ViewModel contexts through a registration port provided by the workbench. |
| `RetroIde.Mcp.Stdio` | CLI attachment to a selected running IDE instance through its local endpoint. Use a per-launch attachment descriptor. Standard output carries only MCP messages. |
| `RetroIde.Adapters.Abstractions` and provider projects | Versioned .NET bundle/adapter contracts and backend mappings as each provider lands. Keep desktop dependencies outside adapter hosts. |
| `RetroIde.UnitTests`, `RetroIde.IntegrationTests`, and `RetroIde.NativeTests` | Criterion-linked tests and negative controls. Use managed, rendered Avalonia, native window, SDK client, and provider tests through the appropriate production entry points. |
| `RetroIde.AdapterFixture` | Test-only independent session process and runtime assembly evidence for shell layout persistence. |

Use the pinned .NET 10 SDK and centrally pinned packages for Windows and Linux. Apply `Meziantou.Analyzer` centrally to every .NET project, including tests and providers. Keep any Nuke project outside the product solution. Compile public XML documentation and verify public contract and test links against the QBrain criteria on the local validation hosts. Do not create GitHub Actions workflows for this project. Use the existing desktop designs as the interaction reference. [Wireframes and storyboards](../design/_manifest.md)

## Local verification for every phase

- Build Release binaries and compiled traces on LAB-OMARCHY. Build Windows test artifacts per project for `win-x64`, then copy a hash-verified runtime bundle to LEGION2 and execute the managed and RemoteControl suites there. Keep Windows restore and compilation on Linux. Run Linux managed and platform integration on LAB-OMARCHY. Use the shared analyzer configuration across all .NET projects. Keep GitHub Actions workflows out of the repository.
- Use SharpNinja.Avalonia.RemoteControl for repeatable UI workflows. Drive the Avalonia control tree, commands, and application-scoped input from the test runner. Use native windows where the feature depends on top-level window behavior, and capture the visible state, effect, and failure feedback. On Linux under Wayland, give Xvfb an explicit unused display number and verify that its socket and lock are absent before launch.
- Use fixtures and real SDK clients for service, protocol, and negative-control evidence. Use installed Git and language servers or shipped emulator providers for the corresponding integration gates. Drive guest input through the authorized emulator port. Desktop UI testing and provider conformance have separate evidence because they exercise different boundaries.
- Verify each acceptance criterion at the boundary it specifies. For a criterion fully covered by a production CQRS handler, give HV the handler test, its assertions, and the resulting state or output. Handler-level evidence can satisfy that criterion without UI validation. Criteria that require desktop interaction, rendered feedback, or native window behavior need UI evidence. Criteria that require an MCP transport need evidence from that transport.
- Windows acceptance uses the input-free RemoteControl suite on real Win32 windows. An interactive pointer smoke run is supplemental. Capture app-level hit testing, the platform cursor assigned by Avalonia, and responsive controls during a scoped wait. Linux keeps its isolated X11/XTEST gesture and effective-cursor regression coverage. Report the observation each runner actually made.
- Independent review runs the same agreed proof on the applicable local hosts. A missing tool or optional supplement does not create a phase gate. If a required product behavior has no valid automated observation, record that specific evidence gap in QBrain before changing the gate.

## First executable increment

Launch the workbench, float and redock a tool pane, and focus it from the IDE. Then inspect the live `WorkbenchViewModel` context and use it to invoke the same focus operation from stdio and Streamable HTTP. Compare visible focus, modeled result, and revision from the same starting state. Repeat with a stale revision and a closed or replaced registration. Expect a structured rejection and unchanged state.

| Boundary | Phase 01 contract |
| --- | --- |
| Live view | `WorkbenchViewModel : ObservableObject, IView` implements the IDE's extension of `GPS.SimpleMVC.Views.ISimpleView`. It registers directly with the main `SimpleControllerBase` controller. A unique `ViewKey` identifies that registration. |
| UI binding | Menu, command bar, and shortcut use one Toolkit `ICommand` per activation scope for `workbench.view.focus`. Its descriptor supplies label slug, icon slug, and default gesture. `CommandRequested` captures the request ID, command ID, source `ViewKey`, context, and cancellation when applicable. The registered owner subscribes once and detaches on unregister. |
| MCP context | The public members are `ViewKey`, `WorkspaceId`, `ActiveToolId`, and `WorkbenchRevision`. A missing active tool is an explicit null. Explicit exposure metadata on those members drives schema and read-only snapshot serialization. Capture on `Dispatcher.UIThread` and scope the snapshot to the authenticated caller. The schema allowlist excludes commands, callbacks, services, and credentials. |
| Typed requests | `workbench_get_context` reads the live snapshot and `workbench_focus_tool` focuses the requested pane. Focus carries its target `ToolId` as typed operation input and the ViewModel identity and expected revision as context. UI and MCP use the same query or command and Unit of Work handler. |
| Dispatch and response | Resolve the instance, workspace, live view, and capability grants. Check revision at the write boundary so racing requests with the same revision cannot both apply. Return correlated IDs, revision, result, or stable error. The registered controller delivers UI results to the originating live view on Avalonia's dispatcher. MCP returns to its caller. Both routes publish observable model changes on the UI thread. |
| Transport | Official `ModelContextProtocol` stdio attach and `ModelContextProtocol.AspNetCore` loopback Streamable HTTP target the same IDE and registry. Verify client grants, loopback binding, token, and applicable Origin. Revocation and instance shutdown invalidate attachment. |

The first increment exercises one dispatch per invocation, hidden-member exclusion, result routing after focus changes, stale context, cross-workspace rejection, invalid HTTP Origin, and stdio attachment to the already-running workbench. It also verifies split-tab persistence and the state and assembly isolation of a separate adapter fixture across workbench close and reopen.

| Slice and QBrain item | Acceptance proof |
| --- | --- |
| **01A** · `EXEC-TODO-001` | Prove `TEST-RETRO-001#verify-ac-behavior-desktop-1` and `verify-ac-contract-desktop-1`. In `RemoteControlDockingTests`, the toolbar selects the real tool tab and Dock floats it into a distinct Win32 window and redocks it into the original group. On isolated Linux, `NativeDockingTests` also exercises drag-out and redock gestures. `ShellLifecycleTests` checks the adapter fixture dependency manifest and loaded assemblies in a separate process. |
| **01B** · `EXEC-TODO-002` | `WorkbenchDispatchTests` and the rendered controls exercise Toolkit command parity, sole owner subscription, typed CQRS, cancellation, and result delivery to the live originating view. Accepted in QBrain. |
| **01C** · `EXEC-TODO-003` | `ViewModelContextContractTests`, `McpWorkbenchTransportTests`, and `ProductionAttachmentTests` prove schema parity, scope, and focus through the UI and both SDK transports on the same workbench. Accepted in QBrain. |
| **01D** · `EXEC-TODO-004` | Verify layout and selection restoration across close and restart with an independent running fixture. On both hosts use RemoteControl to verify the rendered docked and floating `StandardCursorType.Wait` scope, available UI actions, overlapping blocks, and restoration after all outcomes. Prove bundled English descriptor and keyboard parity. |

## Phase 01 independent acceptance

| Slice | QBrain item | Verdict | Accuracy | Completeness |
| --- | --- | --- | --- | --- |
| 01A | EXEC-TODO-001 | AGREE | 100 | 100 |
| 01B | EXEC-TODO-002 | AGREE | 100 | 100 |
| 01C | EXEC-TODO-003 | AGREE | 99 | 99 |
| 01D | EXEC-TODO-004 | AGREE | 100 | 100 |

## Iterations and exit evidence

Phase order is `01 → 02 → 03 → 04 → 05 → 06 → 07 → 08`. Phase 02 provides workspace and document identity to Phase 03. Phase 04 provides the pinned provider contract to Phases 05–07. Prepare Git, LSP, and emulator fixtures independently when their contracts are stable. Each feature slice registers its supported semantic operations, declares public ViewModel context, and verifies each criterion at its required boundaries. Phase 08 audits the full registry, including workbench operations beyond the Phase 01 focus slice.

### 01 — Avalonia workbench and public-context MCP

- Scaffold the projects and managed build, then implement the first increment. Add the IDE layout with shared menu/command bar, major document and Emulation tabs, docked and floating panes, and save/restore. Keep a live registration independent of focus and placement. Restore layout without changing emulator state. Prove STB01 with the local native-window suites and the independent adapter fixture.
- Deliver command descriptors, eligibility, a bundled English catalog, typed MVC events, and a scoped system wait cursor. Track overlapping interaction blocks per operation. Available input and paint stay responsive. Clear cursor overrides after success, cancellation, and failure.
- Add the registry-coverage gate, public context projection, and both transports. Verify schema parity, one handler per request, live context, capability checks, rejection without effects, and visibility of model changes. Target-scoped panes, production reattach, and full tool inventory close in later phases.

### 02 — Project Explorer, tabbed editor, and localization

- Add mixed project/folder roots with stable identities, lazy tree loading, persisted state, and coalesced filesystem refresh. Open and reveal files via modeled context and MCP. Prove folder-only and mixed roots, unavailable roots, same-name paths, targeted refresh and stale listing rejection with `ExplorerWorkspaceTests` and RemoteControl.
- Add AvaloniaEdit tabs that register directly with `DocumentController`. Implement edit, save, close, find/replace, syntax mode, per-tab undo and dirty state, and source navigation. Save captures buffer contents and revision. `DocumentRevisionTests` exercise switching tabs during save, a later edit, a failed save, a closed view, independent undo/caret/selection across float and redock, and source navigation from a diagnostic fixture. Prove the Explorer and editor portions of STB01 and STB14 locally. Live build diagnostics arrive in Phase 05 and debugger navigation and gutter breakpoints close in Phase 06.
- Implement typed per-slug text/image resolution, locale-parent-English fallback, persisted language switching, plural/select formatting, RTL, and shortcut validation. `LocalizationWorkflowTests` cover partial language packs, invalid paths and message patterns, fallback diagnostics, pseudo-localized labels, and live docked and floating windows.
- Run `RemoteControlWorkflowTests` through the native runner’s `--remote-control` mode on both hosts. The walkthrough opens same-name files in separate roots, edits and searches a source tab, verifies state in a separate native floating window, saves and reveals the original file, refreshes an external file addition, and restores the workspace and Arabic locale after reopening.

| Phase 02 slice | QBrain task | Required evidence |
| --- | --- | --- |
| 02A · Project Explorer | `EXEC-TODO-005` | Mixed roots, keyboard and pointer open, Reveal, scoped branch refresh, restoration, and rejection of results from a retired workspace. |
| 02B · Source tabs | `EXEC-TODO-006` | AvaloniaEdit buffers and grammar fallback, registered editor ownership, Save across a tab switch and close, and fixture source navigation. Live build and debugger integration remain in Phases 05 and 06. |
| 02C · Localization | `EXEC-TODO-007` | Typed catalog validation and fallback, named formatting, live language changes in native windows, persistence, and stable shortcuts. |

Use `ProductionAttachmentTests` to verify the Phase 02 tool schemas and outcomes over stdio and Streamable HTTP against one running desktop. The peer desktop verifies isolation. Run the compiled test projection with `eng/Verify-Phase01Trace.ps1 -ThroughPhase 02`.

#### Phase 02 verification status

The renewed delivery at `61a312e` passed 130 managed tests per host, Linux RemoteControl and native X11 checks, and Win32 RemoteControl using binaries built on Linux. The compiled gate verified 88 links across 24 QBrain criteria and 32 public contracts. The independent review returned AGREE at 99 accuracy and 99 completeness for the phase and all three slices. No Phase 01 task regressed. [Accepted verification](../receipts/phase-02-workbench-explorer-editor.md)

| Slice | Accepted verification |
| --- | --- |
| 02A · Project Explorer | Five focused cases covered keyboard and pointer opening and Reveal. Six covered workspace restoration and a retired directory listing. Seven covered targeted external changes, collapsed branches and unavailable roots. |
| 02B · Source tabs | Diagnostic-fixture line and column navigation passed five focused cases. Independent buffer, caret, selection and undo through float and redock, find/replace, and syntax fallback passed 11 focused cases. |
| 02C · Localization | Pack validation passed seven focused cases. Isolated builds also rejected a missing bundled command icon and a missing pane title. Eleven cases covered live labels, icons, accessibility, RTL, stable shortcuts, expanded pseudo-localized labels, and persisted locale. |

All seven incomplete test-first entries from `60457dc` now have criterion-specific proof before the renewed delivery. Phase 02 accepts diagnostic-fixture navigation. Live build diagnostics are in Phase 05, and debugger source navigation and gutter breakpoints are in Phase 06. [Phase 02 C4 snapshot](../architecture/structurizr/versions/implementation-02-workspace-editor-localization/manifest.json)

### 03 — Git, terminal, and language servers

- Use native Git for discovery, clone/init, repository and worktree selection, diffs and hunks, stage and unstage, commit and amend, history and blame, branches/tags/upstreams, fetch/pull/push, stash and patches, merge/rebase/cherry-pick/revert/reset, worktrees, submodules, installed LFS, and a repository-scoped terminal. Preserve native credentials, SSH, prompts, signing, and hooks. Parse machine-readable state with literal paths, revalidate hunks, serialize overlapping writes, and keep unsaved buffers separate. Run `GitWorktreeIntegrationTests` with local bare remotes, unusual paths, divergence, stale hunks, external changes, conflict recovery, and cancellation. [Git integration](../git-integration.md)
- Configure LSP servers by root and language with framed JSON-RPC, negotiated 3.18/3.17 capabilities and encoding, versioned unsaved buffers, and cancellation/restart. Implement advertised completion, hover, symbols, references, definition navigation, diagnostics, formatting, rename, and code actions. Validate workspace edits and route them through the owning controller and CQRS. Run `LanguageServerIntegrationTests` with a deterministic fixture, clangd and rust-analyzer, Unicode, capability changes, stale replies, and recovery while editing remains available. Use RemoteControl for Source Control, terminal and editor surfaces. Exercise scoped MCP parity for Git, terminal and LSP. Introduce the common operation handle for long calls in this slice, with status, result, and cancellation reused by later phases. [Language server contract](../language-servers.md)

#### Phase 03 independent review

The 2026-11-27 independent review of `ca9ae78` accepted Phase 03 at 99 accuracy and 98 completeness. It verified the shared checkout identity guard and confirmed that four queued branch operations and both patch layers leave earlier Git metadata and working files unchanged when the checkout is replaced. Accepted workbench, editor, language-server, and operation-handle behavior passed regression review. [Acceptance evidence](../receipts/phase-03-git-terminal-language-servers.md) · [Independent verdict](../receipts/hv/20261127T132143-phase-03-accepted.verdict.md) · [Previous mutation identity review](../receipts/hv/20261127T120307-phase-03-mutation-identity-review.verdict.md) · [Language-server acceptance](../receipts/hv/20261127T092733-phase-03-endpoint-review.verdict.md)

| Task | Verdict | Accuracy | Completeness | Status |
| --- | --- | --- | --- | --- |
| 03A · `EXEC-TODO-008` | AGREE | 99 | 98 | Complete. Reviewed Git writes revalidate the checkout after waiting. Both native hosts passed the full Git and terminal scope. |
| 03B · `EXEC-TODO-009` | AGREE | 99 | 99 | Complete. The live workbench and framed server observed successful saves on first synchronization and before editor close. The earlier coalesced save cases passed. |
| 03C · `EXEC-TODO-010` | AGREE | 99 | 99 | Complete. Scoped native success, cancellation, failure and foreign-result denial passed across HTTP and stdio. |

Negotiated MCP Tasks and bounded diagnostic or image results remain in Phase 07. The complete MCP inventory and scope audit remain in Phase 08.

### 04 — Emulator manager and adapter conformance

- Implement Git/Scoop-style bucket discovery, manifests, platform selection, hash-checked installs, version pins, receipts, cached/offline use, and transactional recovery. Verify immutable manifest revisions, archive path and entry-point checks, malformed and interrupted installs, side-by-side versions, leases that protect running versions, offline launches, rollback, and incompatible host or contract rejection in `EmulatorBundleIntegrationTests`. Use RemoteControl for Emulators and authorized MCP for the same package operations.
- Implement published adapter and session contracts, typed capability ports, and event/stream semantics. Run `AdapterConformanceTests` against a deterministic adapter for launch and attach close policies, actor and revision, unknown mutation outcome reconciliation, ordered event gaps and resumption, bounded media backpressure, disconnect and reconnect, and compatibility with unknown optional extensions. Bind Emulators UI and MCP tools to the same resolver. [Adapter contract](../emulator-adapter-contract.md)
- Define the optional versioned machine descriptor and verified package profile assets. Use `MachineDescriptorConformanceTests` and `MachineDescriptorCompatibilityTests` for exact CPU identity, map limits and revisions, scope, profile references, and legacy raw inspection. Verify the installed package selection in STB17. [Debugger descriptor contract](../emulator-adapter-contract.md#debugger-machine-description)

### 05 — Build, target launch, and real-backend lifecycle

- Implement Build Project and Start Debugging with captured build configuration, progress, diagnostics, and target resolution. Build alone leaves the emulator stopped. Start Debugging launches the captured debug-capable profile only after build success. Verify failed, cancelled, and stale operations in `BuildLaunchWorkflowTests`. Use RemoteControl to drive the desktop commands and compare progress, source diagnostics, selected-session state, and CQRS results with both MCP transports.
- Integrate UAE Developer's control host first, then VICE `x64sc`/binary monitor and ViceSharp gRPC. Use pinned bundles or configured endpoints. Exercise launch/attach, real session reattach, backend isolation, capability negotiation and permissions through the same conformance suite. Run STB02, STB03, STB14 and STB16. Check neutral preset validation, provider-specific payload validation, launch/attach lifecycle, reattachment, isolated faults, and control loss. Exercise an additional adapter through the same registry. Record each shipped backend capability with a real operation receipt, distinguishing unavailability from connection or launch failure.
- Verify each shipped adapter's machine descriptor path and its normalized CPU, address-space, map, timing capability, and byte-order defaults when advertised. Exercise the retained raw inspection path for a provider with available raw reads and no compatible descriptor.

### 06 — Debugger, guest console, and symbols

- Add machine-neutral debugger tools and typed views for supported registers, memory, disassembly, trace, breakpoints, watchpoints and stepping. Route gutter actions and source locations through DocumentController. Retain raw debugger use when symbols are missing.
- Apply the target descriptor to versioned CPU metadata for all seven QBrain families and to platform disassembly and memory layouts. Verify reference and observed timing separately, bounded banked and segmented views, live map changes, and persisted Emulator default, Little-endian, and Big-endian display choices. Exercise `CpuFamilyDebuggerTests`, `CpuClockPhaseTests`, `PlatformDebuggerViewTests`, `MachineMemoryMapTests`, and `DebuggerByteOrderTests`. [STB17](../design/_manifest.md#stb17-package-debugger)
- Add scoped guest, process, worker, and serial consoles, with distinct early boot, guest log, and host event sources. Negotiate optional 68040/AGA and guest runtime extensions at the adapter boundary. `DebugNavigationIntegrationTests` exercise live supported ports, scope and revision failures, unavailable capabilities, and symbol fallback. Verify STB05, STB06, STB11 and STB12, using RemoteControl for the desktop routes and both transports for the registered debugger tools. Verify editor breakpoint navigation and retained raw addressing or disassembly when source mappings are missing or mismatched.

### 07 — Emulation player, media, snapshots, and tests

- Deliver frame capture and presentation, bounded audio queues, host volume, scoped guest keyboard and pointer capture/release, and replay. Verify known frames and audio with adapter-described timing and formats, distinct instance routing, frame-delivery and audio-underrun observations where available, and responsive paint under backpressure. Use RemoteControl to move the playhead on the progress bar beneath the time marker and change volume independently. Verify timestamp and frame updates for seekable recordings and elapsed time with disabled seeking for live sessions.
- Deliver media mount and safe ejection with policy-controlled confirmation, snapshot compatibility and capture/compare/restore, deterministic tests, artifact provenance, and diagnostics. Keep failed or incomplete snapshots unavailable for restore and preserve session context while opening diagnostic or replay results. Extend the shared operation handle with bounded reads, image capture, progress, result, and cancellation. Exercise MCP Tasks when negotiated and the ordinary operation-handle path with both client modes. Run `EmulationPlayerIntegrationTests` and `MediaSnapshotTestWorkflowTests` using STB04, STB07, STB09, STB10 and STB15. Verify both client modes, interruption, stale requests, scope, and backend authority.

### 08 — Agent workbench, full parity, and release evidence

- Add configured CLI agent and external desktop attach workflows, agent activity, grants/revoke, and reconnect. Use RemoteControl for agent configuration, activity, and reconnect. Verify CLI and desktop SDK clients, observer-write denial, invalid HTTP Origin, scoped audit results, capability notifications, grant/revoke, descriptor cleanup, and emulator independence after agent or transport loss through STB08 and both STB13 paths.
- Run `McpFeatureCoverageTests` and `ReleaseWorkbenchAcceptanceTests` across representative calls from every published feature group over both transports, all registration mappings, context schema parity, applicable Tasks negotiation, and revoked/cross-scope requests. Package and install the desktop and CLI attach entry point on Windows and Linux. Run the release walkthrough with RemoteControl and the SDK clients against the installed build on both hosts. Record native window behavior, scoped views, backend capability receipts, and layout and bundle recovery through upgrade. A human walkthrough is supplemental.

## QBrain traceability

The final column assigns the evidence and closeout phase for each QBrain mapping. Earlier slices can close individual criteria. At slice planning, retain each canonical criterion ID and map it to its fixture, production entry point, success case, negative control, and evidence receipt. Close requirements only after all applicable criteria have verified evidence or a recorded requirement decision.

| Functional | Technical | Testing | Phase evidence and closeout |
| --- | --- | --- | --- |
| FR-RETRO-001 | TR-RETRO-DESKTOP-001 | TEST-RETRO-001 | 01 input-free Win32 and isolated Linux docking, independent fixture and layout. 05 live reattach. 08 complete instance-scoped panes. |
| FR-RETRO-002 | TR-RETRO-PROFILE-002 | TEST-RETRO-002 | 05 three presets and guarded lifecycle |
| FR-RETRO-003 | TR-RETRO-AVINPUT-003 | TEST-RETRO-003 | 07 player, frame/audio delivery, scoped input, replay seek, volume, and backpressure. |
| FR-RETRO-004 | TR-RETRO-DEBUG-004 | TEST-RETRO-004 | 06 debugger, console, symbols, and guest inspection |
| FR-RETRO-005 | TR-RETRO-WORKFLOW-005 | TEST-RETRO-005 | 07 media, snapshots, tests, and artifacts |
| FR-RETRO-006 | TR-RETRO-AGENT-006 | TEST-RETRO-006 | 08 agents, scope, and reconnect |
| FR-RETRO-007 | TR-RETRO-COMMAND-007 | TEST-RETRO-007 | 01 dispatch and scoped Wait. 02 ownership and MDI save. 05 build and debug parity and live capability changes. |
| FR-RETRO-008 | TR-RETRO-LOCALIZATION-008 | TEST-RETRO-008 | 01 descriptors, 02 full localization |
| FR-RETRO-009 | TR-RETRO-BUNDLE-009 | TEST-RETRO-009 | 04 transactional bundles, 05 provider receipts |
| FR-RETRO-010 | TR-RETRO-ADAPTER-010 | TEST-RETRO-010 | 04 fixture contract. 05–07 provider lifecycle, real capability ports and stream evidence. 08 shipping capability audit. |
| FR-RETRO-011 | TR-RETRO-EDITOR-011 | TEST-RETRO-011 | 02 editor, save and fixture navigation. 05 live build locations. 06 debugger and gutter breakpoints. |
| FR-RETRO-012 | TR-RETRO-EXPLORER-012 | TEST-RETRO-012 | 02 mixed-root tree and editor routing |
| FR-RETRO-013 | TR-RETRO-GIT-013 | TEST-RETRO-013 | 03 native Git, worktree, conflict, and terminal |
| FR-RETRO-014 | TR-RETRO-LSP-014 | TEST-RETRO-014 | 03 language server lifecycle, sync, and editor features |
| FR-RETRO-015 | TR-RETRO-MCP-015 | TEST-RETRO-015 | 01 live workbench context and both transports. 02–08 per-slice parity. 03 shared operation handles, 07 Tasks and bounded results, 08 complete registry and scope audit. |
| FR-RETRO-016 | TR-RETRO-CPU-016 | TEST-RETRO-016 | 06 CPU family models, timing provenance, platform disassembly and memory, and display byte-order preferences. |
| FR-RETRO-017 | TR-RETRO-MACHINE-017 | TEST-RETRO-017 | 04 package and descriptor conformance. 05 shipped adapter evidence. 06 debugger binding, map updates, and legacy raw inspection. |

## BDPv4 execution and acceptance

1. **Plan the slice.** Create the QBrain iteration and executable TODOs from the accepted plan. Persist dependency IDs, touched interfaces, criterion-level test plan, and `PlanningDecision` checkpoints. Reconcile any defective criterion in QBrain before coding the affected behavior. Record the choice of workbench-first sequence, one feature registry, ViewModel-owned schema, and versioned adapter boundaries.
2. **Prove the test.** Write acceptance tests before production changes. Prove they pass with controlled mocks or fixtures and fail with deliberate defects. Bind the same assertions to missing or incorrect behavior at the real entry point and record the expected red. In Phase 01 use stale context, duplicate subscription, and unauthorized context as negative controls.
3. **Build and refactor.** Implement the smallest real behavior, run the focused tests to green, and refactor. Run all current and prior unit tests with zero failures and zero skips. Run `dotnet build RetroIde.sln -c Release`, `dotnet test RetroIde.sln -c Release --no-build`, the checked-in compiled trace against the effective QBrain export, and the applicable local RemoteControl and platform integration suites. Match the evidence for each project to its declared goal. An unavailable required provider remains open in its provider phase.
4. **Independently validate.** Move implementation TODOs to `Validating` with revision, commands, package/runtime/OS versions, test output, counts, and criterion-level evidence. The separate BDPv4 hostile validator checks that the proof covers each criterion at its required boundary and tries to falsify the claims. Use GPT-6.2 SOL High on LEGION2 for Phase 01. Apply QBrain’s independent acceptance threshold to the open slices and treat accepted slices as regression scope. On `DISAGREE`, route requirement defects to planning or implementation defects to the implementer, then rerun the relevant gates. Retain receipts and session records with phase and TODO IDs.
5. **Close and version.** After HV accepts a slice, mark its fully verified functional, technical, and test criteria satisfied in QBrain with their evidence. Read back the criterion flags and update traceability before refreshing the dashboard. Keep criteria with later-phase work open until their full proof passes. Validate the Structurizr workspace and retain a versioned snapshot for each accepted phase. Link the evidence, diagrams, QBrain items, and implemented behavior to that version. For an authorized phase publication, sync C4, sync QBrain logs, refresh documentation and wiki exports, commit and push the checkpoint, publish the GitHub wiki, then refresh the private fr-dashboard with the versioned diagrams. Keep bundle versions and user data recoverable in release upgrade/rollback tests.

## Decisions to prove early

| Decision | First proof and response to a failure |
| --- | --- |
| Native floating windows and gestures | Run STB01 on Win32 with RemoteControl and on a controlled Linux display. Compare real window identities and final Dock ownership. Record Linux drag and effective-cursor observations with the compositor/backend. |
| Same instance on both MCP transports | Prove CLI and desktop clients find the selected instance when two IDEs are running. Exercise client disconnect, cancellation, and registration disposal with correlated logs and dispatcher traces. |
| Public context compatibility | Compare context schemas and serialized snapshots, including field names/types and allowlist exclusions. Keep tool names stable and review public schema changes. Recheck live registration and revision before mutating. |
| Backend capability gaps | Record actual backend/version/host capabilities and conformance evidence. Raise a scoped requirement decision if a required port or supported host cannot be demonstrated. |
