# PLAN-RETROIDE-001 — Retro IDE BDPv4 implementation plan

**Status** • The Phase 01 candidate is implemented and has passed cross-platform functional checks. Independent acceptance remains open. Phases 02–08 remain planned. [Verification](../receipts/phase-01-remotecontrol-verification.md)

**Baseline** — The live Retro IDE QBrain workspace returned 15 functional, 15 technical, and 15 testing requirements linked in 15 sets, with 176 acceptance criteria. All 45 records were pending. The published design is [Retro IDE Architecture](https://github.com/sharpninja/retro-ide/wiki/Architecture), [MCP Tools](https://github.com/sharpninja/retro-ide/wiki/MCP-Tools), and [C4 Model](https://github.com/sharpninja/retro-ide/wiki/C4-Model). Source commit `fecb1622ac493f51e86612ddb30522d8d64b131b`, wiki commit `ad1c2850ce4dc51859295c24504e8cc90adb5ced`, C4 snapshot `design-12-mcp-viewmodel-context`.

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
| `RetroIde.UnitTests` and `RetroIde.IntegrationTests` | Criterion-linked tests and negative controls, then real dispatcher, SDK client, native window, and provider tests through production entry points. |

Use .NET 10 as the proposed baseline. Pin SDK and package versions against the Windows and Linux build in Phase 01. Apply `Meziantou.Analyzer` centrally to every .NET project, including tests and providers. Keep any Nuke project outside the product solution. Compile public XML documentation and link public contracts and tests to QBrain criteria in CI. Use the existing desktop designs as the interaction reference. [Wireframes and storyboards](../design/_manifest.md)

## First executable increment

Launch the workbench, float and redock a tool pane, and focus it from the IDE. Then inspect the live `WorkbenchViewModel` context and use it to invoke the same focus operation from stdio and Streamable HTTP. Compare visible focus, modeled result, and revision from the same starting state. Repeat with a stale revision and a closed or replaced registration. Expect a structured rejection and unchanged state.

| Boundary | Proposed first contract |
| --- | --- |
| Live view | `WorkbenchViewModel : ObservableObject, IView` implements the IDE's extension of `GPS.SimpleMVC.Views.ISimpleView`. It registers directly with the main `SimpleControllerBase` controller. A unique `ViewKey` identifies that registration. |
| UI binding | Menu, command bar, and shortcut use one Toolkit `ICommand` per activation scope for `workbench.view.focus`. Its descriptor supplies label slug, icon slug, and default gesture. `CommandRequested` captures the request ID, command ID, source `ViewKey`, context, and cancellation when applicable. The registered owner subscribes once and detaches on unregister. |
| MCP context | Proposed public members include `ViewKey`, `WorkspaceId`, `ActiveToolId`, and `WorkbenchRevision`. Explicit exposure metadata on those members drives schema and read-only snapshot serialization. Capture on `Dispatcher.UIThread` and scope the snapshot to the authenticated caller. The schema allowlist excludes commands, callbacks, services, and credentials. |
| Typed requests | Proposed tools `workbench_get_context` and `workbench_focus_tool`. Focus carries its target `ToolId` as typed operation input and the ViewModel identity and expected revision as context. UI and MCP use the same query or command and Unit of Work handler. |
| Dispatch and response | Resolve the instance, workspace, live view, and capability grants. Check revision at the write boundary so racing requests with the same revision cannot both apply. Return correlated IDs, revision, result, or stable error. The registered controller delivers UI results to the originating live view on Avalonia's dispatcher. MCP returns to its caller. Both routes publish observable model changes on the UI thread. |
| Transport | Official `ModelContextProtocol` stdio attach and `ModelContextProtocol.AspNetCore` loopback Streamable HTTP target the same IDE and registry. Verify client grants, loopback binding, token, and applicable Origin. Revocation and instance shutdown invalidate attachment. |

Phase 01 test targets are `WorkbenchDispatchTests`, `ViewModelContextContractTests`, `McpWorkbenchTransportTests`, and `NativeDockingTests`. Prove one dispatch per invocation, hidden-member exclusion, correct routing after focus changes, rejection before mutation on stale or cross-workspace context, invalid HTTP Origin, and no second IDE spawned by stdio. Exercise native top-level tear-away/redock on Windows and Linux and record the Linux window-system backend. Use SharpNinja.Avalonia.RemoteControl in the managed Avalonia Headless suite to locate and invoke the production controls without physical keyboard or mouse input. Keep the signed-in Windows desktop smoke test opt-in. Pair these scenarios with deterministic dispatcher and concurrency fixtures.

| Phase 01 slice and prerequisite | Acceptance proof |
| --- | --- |
| 01A — Build and minimal shell | Package restore, analyzers, validated test/trace entry points, and a real workbench process on both hosts. Prove the dockable shell with `TEST-RETRO-001#verify-ac-behavior-desktop-1`. |
| 01B — Shared dispatch and focus, after 01A | Exercise Toolkit binding, owner subscription, cancellation, and result routing against `TEST-RETRO-007#ac-test-simplemvc-views`, `ac-test-cqrs-unit-of-work`, `ac-test-toolkit-bindings`, and `ac-test-view-result-routing`. Use a genuine docked tool as the focus target. |
| 01C — Context projection and transport parity, after 01B | Exercise `TEST-RETRO-015#ac-test-mcp-same-instance` and `ac-test-mcp-viewmodel-context` through both SDK clients. Establish the workspace/Origin/stale guard coverage of `ac-test-mcp-scope` and the inventory check from `ac-test-mcp-inventory`. Later phases supply proof for backend scope and the full feature inventory. |
| 01D — Shell persistence and wait lifecycle, after 01B and 01C | Exercise layout restoration using a running adapter fixture and the native scoped `TEST-RETRO-007#ac-test-system-wait-cursor` workflow. Prove command and shortcut parity with the bundled catalog against `TEST-RETRO-008#ac-test-gesture-parity`. |

## Iterations and exit evidence

Phase order is `01 → 02 → 03 → 04 → 05 → 06 → 07 → 08`. Once the workspace and document contracts are stable, Git, LSP, and bundle fixtures can be prepared independently. Every delivered feature adds its typed registration, MCP schema and scope checks, and acceptance tests in the same slice.

### 01 — Avalonia workbench and public-context MCP

- Scaffold the projects and managed build, then implement the first increment. Add the IDE layout with shared menu/command bar, major document and Emulation tabs, docked and floating panes, and save/restore. Keep a live registration independent of focus and placement. Restore layout without changing emulator state. Prove the native window workflow with STB01.
- Deliver command descriptors, eligibility, a bundled English catalog, typed MVC events, and a scoped system wait cursor. Track overlapping interaction blocks per operation. Available input and paint stay responsive. Clear cursor overrides after success, cancellation, and failure.
- Add the registry-coverage gate, public context projection, and both transports. Verify schema parity, one handler per request, live context, capability checks, rejection without effects, and visibility of model changes. Target-scoped panes, production reattach, and full tool inventory close in later phases.

### 02 — Project Explorer, tabbed editor, and localization

- Add mixed project/folder roots with stable identities, lazy tree loading, persisted state, and coalesced filesystem refresh. Open and reveal files via modeled context and MCP. Prove with `ExplorerWorkspaceTests`.
- Add AvaloniaEdit tabs that register directly with `DocumentController`. Implement edit, save, close, find/replace, syntax mode, per-tab undo and dirty state, and source navigation. Save captures buffer contents and revision. `DocumentRevisionTests` exercise switching tabs during save, a later edit, a failed save, and a closed view. The breakpoint integration closes in Phase 06.
- Implement typed per-slug text/image resolution, locale-parent-English fallback, persisted language switching, plural/select formatting, RTL, and shortcut validation. `LocalizationWorkflowTests` cover partial and pseudo-localized catalogs, invalid packs, and live docked/floating windows.

### 03 — Git, terminal, and language servers

- Use native Git for repository/worktree discovery, diffs/hunks, index and commit, history, refs/remotes, conflict and operation recovery, stashes, patches, worktrees, submodules, LFS, and an integrated terminal. Preserve native prompts, hooks, signing, locks, and dirty buffers. Run `GitWorktreeIntegrationTests` with local bare remotes, unusual paths, stale hunks, conflicts, external changes, and cancellation. [Git integration](../git-integration.md)
- Configure LSP servers by root and language with framed JSON-RPC, negotiated 3.18/3.17 capabilities and encoding, versioned unsaved buffers, and cancellation/restart. Implement advertised completion, navigation, symbols, diagnostics, and code changes. Validate workspace edits through the same document handlers. Run `LanguageServerIntegrationTests` with a deterministic fixture, clangd and rust-analyzer, Unicode, stale responses, and process restart. Exercise scoped MCP access for both Git and LSP. [Language server contract](../language-servers.md)

### 04 — Emulator manager and adapter conformance

- Implement Git/Scoop-style bucket discovery, manifests, platform selection, hash-checked installs, version pins, receipts, cached/offline use, and transactional recovery. Verify malformed and interrupted installs, side-by-side versions, active-session pins, and incompatible host/contract rejection in `EmulatorBundleIntegrationTests`.
- Implement published adapter and session contracts, typed capability ports, and event/stream semantics. Run `AdapterConformanceTests` against a deterministic adapter for launch/attach, revisions and actor scope, operation outcomes, event gaps, backpressure, disconnect, and compatibility with unknown optional extensions. Bind Emulators UI and MCP tools to the same resolver. [Adapter contract](../emulator-adapter-contract.md)

### 05 — Build, target launch, and real-backend lifecycle

- Implement Build Project and Start Debugging with captured build configuration, progress, diagnostics, and target resolution. Build alone leaves the emulator stopped. Start Debugging launches the captured debug-capable profile only after build success. Verify failed, cancelled, and stale operations in `BuildLaunchWorkflowTests`.
- Integrate UAE Developer's control host first, then VICE `x64sc`/binary monitor and ViceSharp gRPC. Use pinned bundles or configured endpoints. Exercise launch/attach, real session reattach, backend isolation, capability negotiation and permissions through the same conformance suite. Use STB02, STB03, STB14 and STB16. An absent capability returns a structured unavailable result. A required feature without a working provider remains open.

### 06 — Debugger, guest console, and symbols

- Add machine-neutral debugger tools and typed views for supported registers, memory, disassembly, trace, breakpoints, watchpoints and stepping. Route gutter actions and source locations through DocumentController. Retain raw debugger use when symbols are missing.
- Add scoped guest, process, worker, and serial consoles. Negotiate optional 68040/AGA and guest runtime extensions at the adapter boundary. `DebugNavigationIntegrationTests` exercise live supported ports, scope and revision failures, unavailable capabilities, and symbol fallback. Verify STB05, STB06, STB11 and STB12, including editor breakpoint navigation and related MCP tools.

### 07 — Emulation player, media, snapshots, and tests

- Deliver frame/capture presentation, bounded audio queues and host volume, input capture and release, and recording replay in the Emulation tab. Include the progress bar and draggable time marker. Seek only eligible recordings and keep paint responsive under feed backpressure.
- Deliver media mount and safe ejection, snapshot capture/compare/restore, deterministic tests, and diagnostic artifacts. Add bounded reads and image capture plus operation status, progress, result and cancel. Use MCP Tasks when negotiated and preserve the ordinary operation-handle path. Run `EmulationPlayerIntegrationTests` and `MediaSnapshotTestWorkflowTests` using STB04, STB07, STB09, STB10 and STB15. Verify both client modes, interruption, stale requests, scope, and backend authority.

### 08 — Agent workbench, full parity, and release evidence

- Add configured CLI agent and external desktop attach workflows, agent activity, grants/revoke, and reconnect. Verify observer-write denial, capability notifications, cleanup, and live backend independence through STB08 and both STB13 paths.
- Run `McpFeatureCoverageTests` and `ReleaseWorkbenchAcceptanceTests` across representative calls from every published feature group over both transports, all registration mappings, context schema parity, applicable Tasks negotiation, and revoked/cross-scope requests. Package and install the desktop and CLI attach entry point on Windows and Linux. Verify layout and bundle recovery through upgrade and run a human walkthrough on each host.

## QBrain traceability

The last column identifies the gate that can supply full evidence for each linked set. Earlier increments exercise a subset. At slice planning, read criterion IDs from QBrain and assign each to the test, fixture, production entry point, expected result, and negative control. Close criteria only on verified evidence or a recorded requirement decision.

| Functional | Technical | Testing | Phase evidence and closeout |
| --- | --- | --- | --- |
| FR-RETRO-001 | TR-RETRO-DESKTOP-001 | TEST-RETRO-001 | 01 docking, 05 reattach, 08 all scoped panes |
| FR-RETRO-002 | TR-RETRO-PROFILE-002 | TEST-RETRO-002 | 05 three presets and guarded lifecycle |
| FR-RETRO-003 | TR-RETRO-AVINPUT-003 | TEST-RETRO-003 | 07 player, streams, replay, seek, and volume |
| FR-RETRO-004 | TR-RETRO-DEBUG-004 | TEST-RETRO-004 | 06 debugger, console, symbols, and guest inspection |
| FR-RETRO-005 | TR-RETRO-WORKFLOW-005 | TEST-RETRO-005 | 07 media, snapshots, tests, and artifacts |
| FR-RETRO-006 | TR-RETRO-AGENT-006 | TEST-RETRO-006 | 08 agents, scope, and reconnect |
| FR-RETRO-007 | TR-RETRO-COMMAND-007 | TEST-RETRO-007 | 01 dispatch and wait, 02 MDI, 05 build and debug |
| FR-RETRO-008 | TR-RETRO-LOCALIZATION-008 | TEST-RETRO-008 | 01 descriptors, 02 full localization |
| FR-RETRO-009 | TR-RETRO-BUNDLE-009 | TEST-RETRO-009 | 04 transactional bundles, 05 provider receipts |
| FR-RETRO-010 | TR-RETRO-ADAPTER-010 | TEST-RETRO-010 | 04 contract, 05–07 live ports and capability proof |
| FR-RETRO-011 | TR-RETRO-EDITOR-011 | TEST-RETRO-011 | 02 editor and save, 06 breakpoint integration |
| FR-RETRO-012 | TR-RETRO-EXPLORER-012 | TEST-RETRO-012 | 02 mixed-root tree and editor routing |
| FR-RETRO-013 | TR-RETRO-GIT-013 | TEST-RETRO-013 | 03 native Git, worktree, conflict, and terminal |
| FR-RETRO-014 | TR-RETRO-LSP-014 | TEST-RETRO-014 | 03 language server lifecycle, sync, and editor features |
| FR-RETRO-015 | TR-RETRO-MCP-015 | TEST-RETRO-015 | 01 live public context and both transports, 02–08 parity, 08 full inventory |

## BDPv4 execution and acceptance

1. **Plan the slice.** Create the QBrain iteration and executable TODOs from the accepted plan. Persist dependency IDs, touched interfaces, criterion-level test plan, and `PlanningDecision` checkpoints. Reconcile any defective criterion in QBrain before coding the affected behavior. Record the choice of workbench-first sequence, one feature registry, ViewModel-owned schema, and versioned adapter boundaries.
2. **Prove the test.** Write acceptance tests before production changes. Prove they pass with controlled mocks or fixtures and fail with deliberate defects. Bind the same assertions to missing or incorrect behavior at the real entry point and record the expected red. In Phase 01 use stale context, duplicate subscription, and unauthorized context as negative controls.
3. **Build and refactor.** Implement the smallest real behavior, run the focused tests to green, and refactor. Run all current and prior unit tests with zero failures and zero skips. Planned commands are `dotnet build RetroIde.sln -c Release`, `dotnet test tests/RetroIde.UnitTests -c Release`, the integration project on Windows and Linux, and a requirement-trace target introduced in Phase 01. Capture native UI and real-provider evidence in addition to headless evidence. A missing runner or fixture leaves its gate open.
4. **Independently validate.** Move implementation TODOs to `Validating` with revision, commands, package/runtime/OS versions, test output, counts, and criterion-level evidence. The separate BDPv4 hostile validator reruns the gates and attempts to falsify the claims. Complete only with `AGREE` and passing applicable evidence. On `DISAGREE`, route requirement defects to planning or implementation defects to the implementer, then rerun the relevant gates. Retain receipts and session records with phase and TODO IDs.
5. **Close and version.** Record acceptance and update QBrain traceability. Validate the Structurizr workspace and retain a versioned snapshot for each accepted phase, identifying planned versus implemented behavior. Keep bundle versions and user data recoverable in release upgrade/rollback tests.

## Decisions to prove early

| Decision | First proof and response to a failure |
| --- | --- |
| Native floating windows and gestures | Run STB01 on Windows and supported Linux desktop sessions before expanding the workbench. If redock fails on an intended Linux session, record the compositor/backend and resolve the affected criterion before phase acceptance. |
| Same instance on both MCP transports | Prove CLI and desktop clients find the selected instance when two IDEs are running. Exercise client disconnect, cancellation, and registration disposal with correlated logs and dispatcher traces. |
| Public context compatibility | Compare context schemas and serialized snapshots, including field names/types and allowlist exclusions. Keep tool names stable and review public schema changes. Recheck live registration and revision before mutating. |
| Backend capability gaps | Record actual backend/version/host capabilities and conformance evidence. Raise a scoped requirement decision if a required port or supported host cannot be demonstrated. |
