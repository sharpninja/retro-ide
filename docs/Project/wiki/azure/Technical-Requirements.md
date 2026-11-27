# Technical Requirements (QBrain.AI)

## TR-RETRO-ADAPTER-010

**Versioned .NET session contract and capability ports** — Define the shared .NET adapter factory, session identity and lifetime, optional capability ports, request and event semantics, and media stream boundaries.
**Covered by:** FR: FR-RETRO-010; TEST: TEST-RETRO-010
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Check the bundle adapter identity and supported contract versions. Negotiate a compatible session with stable identity, lifecycle, state revision, capabilities, an event cursor, and an explicit close policy for launch versus attach.
- [ ] Discover typed capability ports with negotiated versions and limits for execution, debugger and inspection, video, audio, input, media, snapshots and replay, console and diagnostics, and guest tools. Unknown optional extensions preserve common session behavior.
- [ ] Route scoped operations with correlated IDs and expected revisions where applicable. Normalize typed outcomes and errors while preserving backend authorization. Reconcile uncertain mutation outcomes before unsafe retries.
- [ ] Expose ordered control events with cursor resumption or explicit gap recovery. Keep bounded and separately timestamped media streams independent of control traffic and release subscriptions and bundle leases on session disposal.
- [ ] Document the UAE control, VICE binary monitor, and ViceSharp gRPC mappings. Distinguish frame capture from continuous video, and only advertise verified capabilities.

## TR-RETRO-AGENT-006

**Integrated agent activity and scoped control contract** — Use local MCP stdio for a launched agent and shared instance identity and authorization across IDE and headless clients.
**Covered by:** FR: FR-RETRO-006; TEST: TEST-RETRO-006
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Agent activity respects host capabilities and permissions and does not introduce a path around the adapter.

## TR-RETRO-AVINPUT-003

**Emulation video, audio, input, and replay controls contract** — Adapt media streams and input through capability-driven contracts. Keep presentation and host playback controls independent of the underlying transport.
**Covered by:** FR: FR-RETRO-003; TEST: TEST-RETRO-003
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Use adapter-described dimensions, format, timing, and input routing. Prevent media, input, and playback state from bleeding between instances.

## TR-RETRO-BUNDLE-009

**Emulator bundle publishing and installation contract** — Read versioned JSON manifests from configured Git buckets, verify host-specific release archives, and resolve installed bindings through pinned receipts.
**Covered by:** FR: FR-RETRO-009; TEST: TEST-RETRO-009
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Each emulator repository entry is bucket/<id>.json at a recorded Git commit. It declares schema version, stable bundle ID, immutable release version, display and license metadata, adapter identity and compatible contract, and runtime-identifier targets with archive URLs, SHA-256 and relative entry points.
- [ ] Select a compatible platform artifact, verify SHA-256, validate archive paths and entry points, and publish the staged files and receipt atomically. A failed or canceled operation leaves verified versions usable.
- [ ] Resolve emulator binaries and bindings from a compatible receipt matching the project pin, with a clear diagnostic for missing or incompatible versions. A live session holds its installed bundle version and prevents its removal.

## TR-RETRO-COMMAND-007

**SimpleMVC command dispatch and CQRS Unit of Work** — Use CommunityToolkit.Mvvm for observable IView ViewModels and RelayCommand or AsyncRelayCommand bindings. Each view registers directly with its dispatching SimpleMVC controller, which handles its events, dispatches typed requests through the main controller shared CQRS Unit of Work entry point, and delivers the results through Avalonia.
**Covered by:** FR: FR-RETRO-007; TEST: TEST-RETRO-007
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [x] The view binds equivalent entries to a shared ICommand with a stable ID. CanExecute reads eligibility published by the Model, and CanExecuteChanged refreshes the bound controls. (evidence: 2026-11-26 Phase 01B AGREE 100/100 and Phase 01D AGREE 100/100. Shared Toolkit command, descriptor label and icon, desktop bindings and duplicate-gesture rejection were verified with passing managed and native tests. docs/receipts/phase-01-workbench-mcp.md.)
- [x] The main controller derives from GPS.SimpleMVC.Controllers.SimpleControllerBase and provides the shared execution entry point to the custom CQRS Unit of Work. Dispatch owners use that entry point with the typed request and captured context. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [ ] A stale command context or an unsupported capability produces no emulator mutation. The adapter host remains authoritative for authorization.
- [x] Use the GPS.SimpleMVC NuGet package. Every IDE ViewModel implements Retro IDE IView, which extends GPS.SimpleMVC.Views.ISimpleView. Each live ViewModel has a unique ViewKey and participates in the SimpleMVC view registry. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] All IDE application logic uses the custom CQRS Unit of Work dispatched by the main MVC controller. The Unit of Work scopes each typed request and invokes a registered handler. Queries return read-only results. Commands validate context and publish operation progress and outcomes to the Model. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] Every IDE ViewModel derives from CommunityToolkit.Mvvm ObservableObject, uses generated observable properties for UI state, and implements IView for SimpleMVC registration. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] Shared UI commands use RelayCommand or AsyncRelayCommand. Task-based commands await completion, expose IsRunning, suppress repeat activation by default, and forward supported cancellation tokens. Eligibility changes invalidate CanExecute through the Toolkit API. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] The controller a view registers with subscribes once and detaches on removal. Toolkit commands raise CommandRequested on the originating registered view with a request ID, ViewKey, command ID, captured context, and any applicable cancellation token. Its dispatch owner verifies the sender and selects the typed CQRS request. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] The dispatch owner schedules correlated success, failure, and cancellation on the Avalonia UI dispatcher. The owner checks that its originating registration is still live in the callback, then updates that view. Shared model state remains observable independently. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] Each live IView registers directly with one SimpleMVC controller. That registration determines the dispatch owner and the sole CommandRequested subscription. The owner dispatches the request and delivers the result. Multiple controllers can own distinct views at the same time. (evidence: 2026-11-26 Phase 01B AGREE 100/100. The reviewer verified SimpleMVC ownership and registration, typed CQRS, one dispatch per request, Toolkit eligibility and cancellation, and Avalonia results delivered to the originating live view. See the accepted 01B findings in docs/receipts/phase-01-reentrant-layout-hv.md.)
- [x] In the tabbed MDI reference design, each editor tab implements IView and registers directly with DocumentController. The active tab supplies the document command binding. The chosen command captures the document ID and editor registration at invocation. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-006. Delivery 61a312e. editor-buffer-state-secondary, diagnostic-navigation, original Save correction and editor ownership. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Publish interaction blocks by operation and UI scope. Apply Avalonia StandardCursorType.Wait to the affected surfaces on the UI thread, keep paint and available input responsive, and clear the temporary override once all covering operations release their blocks on completion, failure, or cancellation. (evidence: 2026-11-26 Phase 01D AGREE 100/100. Win32 and Linux RemoteControl verified the Avalonia Wait overlay, rendering, available commands and restoration. Isolated Linux separately sampled effective OS cursor behavior. docs/receipts/phase-01-workbench-mcp.md.)

## TR-RETRO-CPU-016

**CPU model, debugger rendering, and byte order contract** — Keep reusable CPU model and variant definitions separate from live emulator observations. Render normalized debugger data using the selected platform display profile and a scoped byte-order preference.
**Covered by:** FR: FR-RETRO-016; TEST: TEST-RETRO-016
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Resolve a declared CPU family, exact model, and variant to register and flag widths, instruction encodings, addressing modes, byte order, architectural address limits, and timing and clock-phase semantics. Preserve explicit unknown or unsupported values and identify metadata version.
- [ ] Represent nominal, conditional, and observed timing separately. Normalize timestamps or cycle counts in the relevant clock domain and describe phase or bus events only when the provider supplies them. Preserve negotiated operation capabilities.
- [ ] Use versioned platform display profiles for shared disassembly rows and memory groups. Preserve stable CPU, address-space, bank or segment, and location identity across view and tool modes. Render scoped bounded ranges and adopt the corresponding address notation and column defaults without hard-coded backend names.
- [ ] Resolve the effective display byte order from an explicit Little-endian or Big-endian user preference, otherwise the selected address space default, otherwise the declared emulator machine or CPU default. Preview typed multibyte values accordingly while preserving bytes, actual CPU configuration, and instruction decoding. When the provider cannot supply the default, surface it as unknown until the user chooses a display interpretation.

## TR-RETRO-DEBUG-004

**Debugger, console, and guest inspection tools contract** — Use machine metadata and optional extensions to present address spaces, registers, device inspectors, symbol resolution, and guest features.
**Covered by:** FR: FR-RETRO-004; TEST: TEST-RETRO-004
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] The common shell does not assume 68040, AGA, 32-bit addresses, ELF/DWARF, or an A1200 OS guest.
- [ ] Tag observed state by target and instance. Display raw state safely when an optional interpreter or guest extension is unavailable.

## TR-RETRO-DESKTOP-001

**Dockable cross-platform IDE shell contract** — Use Avalonia and Dock.Avalonia on .NET. Serialize layout separately from emulator state and route instance operations through the adapter boundary.
**Covered by:** FR: FR-RETRO-001; TEST: TEST-RETRO-001
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [x] Run the desktop on Windows and Linux without requiring the adapter host to load desktop dependencies. (evidence: 2026-11-26 Phase 01A AGREE 100/100. The workbench ran on both hosts while a separate adapter fixture process had no desktop dependencies in its manifest or loaded assemblies. docs/receipts/phase-01-workbench-mcp.md.)
- [ ] Restore the docking layout without altering emulator instance state.
- [ ] Reattach to a running instance through the adapter.

## TR-RETRO-EDITOR-011

**AvaloniaEdit editor surface and integration** — Implement the Windows and Linux source surface using AvaloniaEdit.TextEditor from Avalonia.AvaloniaEdit. Integrate it with tabbed MDI and the existing controller and CQRS path.
**Covered by:** FR: FR-RETRO-011; TEST: TEST-RETRO-011
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [x] Host a separate AvaloniaEdit.TextEditor and document buffer per source tab. Provide line numbers, search and replace, supported language highlighting, and a plain-text fallback. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-006. Delivery 61a312e. editor-buffer-state-secondary, diagnostic-navigation, original Save correction and editor ownership. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Use the tab's registered IView and DocumentController for document commands. Capture document ID, buffer, and revision for save and apply results through the Avalonia UI dispatcher. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-006. Delivery 61a312e. editor-buffer-state-secondary, diagnostic-navigation, original Save correction and editor ownership. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [ ] Project diagnostics and debugger locations into the source view. Route gutter requests through the view owner, CQRS, and the selected adapter capability.

## TR-RETRO-EXPLORER-012

**Workspace tree and editor routing** — Implement the dockable project and folder tree using the registered Explorer ViewModel, shared CQRS dispatch, and the existing editor lifecycle.
**Covered by:** FR: FR-RETRO-012; TEST: TEST-RETRO-012
**Status:** completed
Scope: layer-1+
**Acceptance Criteria:**
- [x] Persist project or folder root kind and stable root identity. Address tree items and selected documents by workspace root and path. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-005. Delivery 61a312e. explorer-folder-and-mixed, explorer-persistence, explorer-external-changes. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Route explorer commands and queries through the main controller registration and CQRS. Return directory and open-document outcomes on the Avalonia UI thread. Open or activate the correct AvaloniaEdit tab. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-005. Delivery 61a312e. explorer-folder-and-mixed, explorer-persistence, explorer-external-changes. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Load children on expansion and refresh affected branches on external file changes. Apply async results only to their originating workspace and root. Report missing or inaccessible paths locally. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-005. Delivery 61a312e. explorer-folder-and-mixed, explorer-persistence, explorer-external-changes. See docs/receipts/phase-02-workbench-explorer-editor.md.)

## TR-RETRO-GIT-013

**Native Git execution and workspace state** — Use native Git from typed MVC and CQRS requests with explicit repository and worktree context, usable outcomes and integration with the editor state.
**Covered by:** FR: FR-RETRO-013; TEST: TEST-RETRO-013
**Status:** completed
Scope: layer-1+
**Acceptance Criteria:**
- [x] Discover or configure the installed Git executable and invoke it asynchronously with argument arrays and a captured working directory. Respect native credentials, SSH, hooks, and signing, and omit secrets from logs. (evidence: Phase 03A AGREE 99/98 on ca9ae78. Linux and LEGION2 each passed 284 managed tests and native RemoteControl. See docs/receipts/hv/20261127T132143-phase-03-accepted.verdict.md.)
- [x] Parse stable NUL-delimited state, account for separate worktree indexes and shared Git refs, scope file selection literally, and apply async results to the captured context. (evidence: Phase 03A AGREE 99/98 on ca9ae78. Linux and LEGION2 each passed 284 managed tests and native RemoteControl. See docs/receipts/hv/20261127T132143-phase-03-accepted.verdict.md.)
- [x] Respect Git locking, serialize overlapping IDE writes, refresh after external changes, and revalidate hunk preimages. Keep unsaved buffers distinct from disk and index state. (evidence: Phase 03A AGREE 99/98 on ca9ae78. Linux and LEGION2 each passed 284 managed tests and native RemoteControl. See docs/receipts/hv/20261127T132143-phase-03-accepted.verdict.md.)
- [x] Expose typed outcomes and Git diagnostics. Detect merge and rebase state, verify conflict stages, and refresh authoritative status after success, errors, or cancellation. (evidence: Phase 03A AGREE 99/98 on ca9ae78. Linux and LEGION2 each passed 284 managed tests and native RemoteControl. See docs/receipts/hv/20261127T132143-phase-03-accepted.verdict.md.)
- [x] Start the integrated terminal in the selected worktree using the user's Git environment so advanced Git operations remain accessible and state changes are reflected in the IDE. (evidence: Phase 03A AGREE 99/98 on ca9ae78. Linux and LEGION2 each passed 284 managed tests and native RemoteControl. See docs/receipts/hv/20261127T132143-phase-03-accepted.verdict.md.)

## TR-RETRO-LOCALIZATION-008

**Command descriptors and typed locale catalogs** — Attach label and icon slugs and a default keyboard gesture to every ICommand registration. Use one localizer for typed text and image lookup, locale-aware formatting, fallback, and live presentation updates.
**Covered by:** FR: FR-RETRO-008; TEST: TEST-RETRO-008
**Status:** completed
Scope: layer-1+
**Acceptance Criteria:**
- [x] Each shared ICommand has a stable ID, label slug, icon slug, and a default gesture. Any platform override supplies both the displayed shortcut and the binding. Gesture validation detects collisions in overlapping activation scopes. (evidence: 2026-11-26 Phase 01B AGREE 100/100 and Phase 01D AGREE 100/100. Shared Toolkit command, descriptor label and icon, desktop bindings and duplicate-gesture rejection were verified with passing managed and native tests. docs/receipts/phase-01-workbench-mcp.md.)
- [x] Versioned BCP 47 locale packs resolve typed text or image slugs. Text entries are UTF-8 strings or message patterns. Image entries resolve to packaged SVG or PNG resources inside the pack. Bundled English covers every required slug. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-007. Delivery 61a312e. pack-validation, live-language, original typed fallback and formatting gates. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Lookup tries exact and parent locales, then bundled English, independently for each resource. An unresolved slug produces a placeholder string or fallback icon and a diagnostic identifying the slug and type. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-007. Delivery 61a312e. pack-validation, live-language, original typed fallback and formatting gates. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] The resolver supports named typed arguments, plural and select forms, and culture-aware number and date formatting. It applies plural rules from the resolved translation's locale. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-007. Delivery 61a312e. pack-validation, live-language, original typed fallback and formatting gates. See docs/receipts/phase-02-workbench-explorer-editor.md.)
- [x] Validation checks resource kinds, paths, message arguments, command IDs, and required descriptor fields. The MVC controller dispatches a typed language change through the CQRS Unit of Work to persist and publish culture changes, and open views update bindings and layout direction. (evidence: 2026-11-26 independent Phase 02 AGREE 99/99, EXEC-TODO-007. Delivery 61a312e. pack-validation, live-language, original typed fallback and formatting gates. See docs/receipts/phase-02-workbench-explorer-editor.md.)

## TR-RETRO-LSP-014

**Versioned editor synchronization and LSP lifecycle** — Implement an LSP client against the 3.18 reference with capability-based interoperability for 3.17 servers.
**Covered by:** FR: FR-RETRO-014; TEST: TEST-RETRO-014
**Status:** completed
Scope: layer-1+
**Acceptance Criteria:**
- [x] Route installed servers by workspace root and language. Exchange framed JSON-RPC over stdio with initialize, shutdown, and exit. Handle supported configuration, workspace folders, and capability registrations. (evidence: 2026-11-27 Phase 03B AGREE 99/99 on 023e0d7. Full independent verdict and passing Linux and Windows receipts. docs/receipts/hv/20261127T092733-phase-03-endpoint-review.verdict.md.)
- [x] Synchronize open, changed, saved, and closed documents according to capabilities. Use monotonic document versions, correct file URIs, and the negotiated character encoding with UTF-16 fallback. (evidence: 2026-11-27 Phase 03B AGREE 99/99 on 023e0d7. Full independent verdict and passing Linux and Windows receipts. docs/receipts/hv/20261127T092733-phase-03-endpoint-review.verdict.md. Actual workbench Save and Close sent framed didSave on first observation and before close, with the saved bytes and monotonic versions.)
- [x] Correlate requests and results by server session, workspace, and versioned document. Cancel superseded work, discard stale replies, and deliver results only to the originating registered editor on the Avalonia UI thread. (evidence: 2026-11-27 Phase 03B AGREE 99/99 on 023e0d7. Full independent verdict and passing Linux and Windows receipts. docs/receipts/hv/20261127T092733-phase-03-endpoint-review.verdict.md.)
- [x] Validate workspace edit targets and document versions before routing supported changes through the owning controller and CQRS. Preserve unsaved buffers and advertise only supported edit and failure-handling capabilities. (evidence: 2026-11-27 Phase 03B AGREE 99/99 on 023e0d7. Full independent verdict and passing Linux and Windows receipts. docs/receipts/hv/20261127T092733-phase-03-endpoint-review.verdict.md.)

## TR-RETRO-MACHINE-017

**Versioned debugger target descriptor and map revisions** — Carry a typed machine descriptor over the existing versioned emulator session contract alongside scoped capability and state revisions.
**Covered by:** FR: FR-RETRO-017; TEST: TEST-RETRO-017
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Negotiate a versioned session-bound machine descriptor with stable instance and CPU IDs, an exact model/variant reference, hardware and address-space default byte order, supported clock domains, profile reference, address width and units, explicit region ranges and permissions, and mapping identity for banks, overlays, mirrors or segments when present.
- [ ] Publish the initial descriptor and revision and apply ordered map changes or reload a current snapshot after a gap. Validate selected instance, CPU, address-space scope, range bounds and applicable mapping revision before reads, presentation, or writes. Resolve overlapping regions by an explicit mapping policy and distinguish side-effect-free inspection from device reads.
- [ ] Validate version and descriptor fields before use, reject malformed ranges and ambiguous mappings, and present stable unavailable outcomes for missing descriptions or unsupported optional fields. Continue successfully negotiated common session capabilities and documented safe raw inspection.

## TR-RETRO-MCP-015

**Official MCP .NET hosting and shared CQRS tool contract** — Use ModelContextProtocol, ModelContextProtocol.AspNetCore, and Microsoft hosting extensions for IDE tools. Use the Tasks extension when negotiated.
**Covered by:** FR: FR-RETRO-015; TEST: TEST-RETRO-015
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [x] Host the desktop endpoint with ModelContextProtocol.AspNetCore Streamable HTTP and provide a ModelContextProtocol stdio attachment to the same IDE instance. Validate transport and client scope. (evidence: 2026-11-26 Phase 01C AGREE 99/99. UI and both MCP transports exercised the same selected workbench with typed results, public ViewModel context and scoped grants. See the accepted 01C findings in docs/receipts/phase-01-reentrant-layout-hv.md and production regression in docs/receipts/phase-01-workbench-mcp.md.)
- [ ] Generate tool names and input and output schemas from typed IDE feature registrations. Verify registry coverage and observe supported tool discovery notifications.
- [x] Validate arguments, workspace, target and capabilities before dispatch through the main MVC controller and CQRS. Return the result to the calling tool, and deliver affected model updates on the Avalonia UI thread. (evidence: 2026-11-26 Phase 01C AGREE 99/99. UI and both MCP transports exercised the same selected workbench with typed results, public ViewModel context and scoped grants. See the accepted 01C findings in docs/receipts/phase-01-reentrant-layout-hv.md and production regression in docs/receipts/phase-01-workbench-mcp.md.)
- [x] Enforce workspace and capability grants independently of transport. Validate expected versions where the operation depends on prior state. Return stable result codes, contextual IDs, and audit outcomes. (evidence: 2026-11-26 Phase 01C AGREE 99/99. UI and both MCP transports exercised the same selected workbench with typed results, public ViewModel context and scoped grants. See the accepted 01C findings in docs/receipts/phase-01-reentrant-layout-hv.md and production regression in docs/receipts/phase-01-workbench-mcp.md.)
- [ ] Propagate cancellation and report progress for requested long operations. Use an operation handle with status and cancel tools, and MCP Tasks when negotiated. Support bounded reads and stream or cursor handles for large data.
- [x] Derive MCP context schemas and snapshots from explicitly exposed public ViewModel members. Capture UI-bound values on the Avalonia UI thread. Resolve the authorized live registration and expected revision before dispatching the typed CQRS request. Keep internal commands and services outside the tool context. (evidence: 2026-11-26 Phase 01C AGREE 99/99. UI and both MCP transports exercised the same selected workbench with typed results, public ViewModel context and scoped grants. See the accepted 01C findings in docs/receipts/phase-01-reentrant-layout-hv.md and production regression in docs/receipts/phase-01-workbench-mcp.md.)

## TR-RETRO-PROFILE-002

**Target selection and project launch contract** — Use an emulator-neutral, versioned preset and capability-driven adapter contract.
**Covered by:** FR: FR-RETRO-002; TEST: TEST-RETRO-002
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Validate the shared preset shape independently of adapter-specific payload validation. Keep binaries, ROMs, and endpoint settings external.
- [ ] Distinguish deliberate capability unavailability from adapter connection or launch failures.

## TR-RETRO-WORKFLOW-005

**Media, snapshots, tests, and diagnostics contract** — Use adapter-reported operation progress, status, and artifact identity. Keep artifact storage and emulator-specific compatibility decisions in their owning hosts.
**Covered by:** FR: FR-RETRO-005; TEST: TEST-RETRO-005
**Status:** pending
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Route mutations through authorized instance operations and carry provenance and operation state into the corresponding panels.

