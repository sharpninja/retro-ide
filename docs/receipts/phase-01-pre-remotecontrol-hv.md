# Phase 01 independent validation before RemoteControl

This review covers the candidate immediately before the requested RemoteControl testing path. The exact review request and response are available in the [request record](hv/phase-01-pre-remotecontrol/request.jsonl) and [response record](hv/phase-01-pre-remotecontrol/response.jsonl).

# PLAN-RETROIDE-001 Phase 01 hostile-validation receipt

OverallVerdict: DISAGREE

OverallAccuracy: 75

OverallCompleteness: 88

ValidatorIdentity: ChatGPT-6.2-SOL-HIGH-LEGION2

Task 01A EXEC-TODO-001: DISAGREE (accuracy 83, completeness 86)

Task 01B EXEC-TODO-002: DISAGREE (accuracy 63, completeness 89)

Task 01C EXEC-TODO-003: DISAGREE (accuracy 90, completeness 91)

Task 01D EXEC-TODO-004: DISAGREE (accuracy 67, completeness 86)

The current Windows native run failed on the first toolbar click. The window was active and the button hovered, but dispatch count and revision both stayed at zero. That run stopped before the native shortcut, menu, docking, wait cursor, and two-desktop MCP checks. The current Linux native run and the independently rerun Windows managed suite passed.

All four tasks are open and in flux. No completed or regression-only tasks apply. **FAIL** means the evidence contradicts a behavior claim or the checked-in test omits a required assertion. **UNKNOWN** means the available evidence cannot decide the behavior. For scoring, accuracy is the percentage of decided matrix rows that pass, and completeness is the percentage of rows with a decision. Scores are rounded to whole percentages. The overall score pools the task rows, including shared requirements in each affected slice. There are 27 PASS, 9 FAIL, and 5 UNKNOWN rows. Both scores must reach 98 for AGREE.

## Execution provenance

| Evidence | Observed result |
|---|---|
| Separate Windows host harness, `sha256sum -c` | Passed before and after the independent run. |
| Separate Windows host harness, SDK `10.0.404`, `dotnet build RetroIde.sln -c Release --disable-build-servers` | Build succeeded with 53 warnings and 0 errors. |
| Separate Windows host harness, `dotnet test RetroIde.sln -c Release --no-build` | 2 unit and 19 integration cases passed. Zero failed or skipped. `RetroIde.NativeTests` is a separate executable. |
| Supplied current Linux runner | Release build succeeded with 25 warnings and 0 errors. All 2 unit and 19 integration cases passed. Native XID/X11/XTEST docking, scoped cursor, and two-desktop MCP passed. The reported per-file comparison between the review and tested archives found one difference, in the Linux workflow step environment. Compiled application and test sources matched. |
| Supplied current Windows interactive desktop run, Windows `10.0.26300.0` | HWND/Win32 toolbar timeout with `Window active True, button hovered True, dispatches 0, revision 0`. The cause is unknown. The earlier successful Windows native and two-desktop logs are historical. |
| Supplied trace runner, `pwsh -NoProfile -NonInteractive -File eng/Verify-Phase01Trace.ps1 -QBrainExport docs/requirements/phase-01-test-criteria.yaml` | 27 test links, 9 documented public contracts, and 14 distinct criteria verified. The runner also reported success with the full effective export and rejection after removing `TEST-RETRO-015#ac-test-mcp-scope`. |

I reviewed the supplied snapshot and receipts. The separate harness and supplied runners performed the executions above. The excerpts give success summaries and failure output, with no raw shell exit codes. `.github/workflows/phase01.yml` configures Windows and Linux build, managed test, trace, and native jobs. The execution evidence is from local runners. A public hosted run is outside this acceptance boundary.

I applied the supplied MEMORY-HV-100 scope and infrastructure rules. The full memory record and live session-log interface were unavailable. Under rule 6, the earlier sandbox socket failures carry no penalty where the current managed and Linux evidence resolves the criterion. The original test-first chronology for all four tasks remains **UNKNOWN**. The supplied red controls establish sensitivity for the tested defects.

## Requirements and evidence matrix

### 01A · FR-RETRO-001 / TR-RETRO-DESKTOP-001 / TEST-RETRO-001

| Claim | Result | Source and evidence |
|---|---|---|
| A1. Windows and Linux Release and managed gates | PASS | `RetroIde.sln`, `global.json`, and the project files build on both hosts in the supplied runs. Both managed suites report 21 passing cases. |
| A2. Analyzer and workflow configuration | PASS | `Directory.Packages.props` centrally references `Meziantou.Analyzer` 3.0.320. `Directory.Build.props` enables XML documentation. `.github/workflows/phase01.yml` configures both host jobs. The captured build summaries omit individual warning diagnostics. |
| A3. Real Avalonia/Dock shell and Linux native docking, `verify-ac-behavior-desktop-1` | PASS | `Program.cs`, `WorkbenchApp.cs`, `WorkbenchWindow.BuildSurface`, and `DockLayout.CreateRoot` instantiate the desktop, Dock control, document groups including Emulation, and native floating hosts. `NativeDockingTests.DragToolAsync` selects Call Stack, tears Registers into a distinct native top-level, then redocks it into the original group. Current Linux output reaches the final success message. |
| A4. Current Windows native toolbar entry point | FAIL | `tests/RetroIde.NativeTests/NativeDockingTests.cs`, `InvokeWorkbenchBindingsAsync`, times out after native input with zero dispatches. The successful Windows headless toolbar test exercises a different input path. |
| A5. Current Windows native shell workflow, `verify-ac-behavior-desktop-1` and desktop process evidence for `verify-ac-contract-desktop-1` | UNKNOWN | The current HWND fixture displayed the shell. Its failure in `InvokeWorkbenchBindingsAsync` prevented current Windows native tab, tear-away, redock, and production desktop-process evidence from this run. |
| A6. CI receives the QBrain criterion input | PASS | `.github/workflows/phase01.yml` passes `-QBrainExport docs/requirements/phase-01-test-criteria.yaml`. Both supplied trace runs and the missing-criterion control support this correction. |
| A7. Phase 01 dependency boundary, `verify-ac-contract-desktop-1` | PASS | `RetroIde.Application.csproj`, `RetroIde.Mcp.csproj`, and `RetroIde.Mcp.Stdio.csproj` keep desktop references on the workbench side. `WorkbenchSession` accepts its workspace context at construction. The running-adapter fixture proof is in D4. |

### 01B · FR-RETRO-007 / TR-RETRO-COMMAND-007 / TEST-RETRO-007

| Claim | Result | Source and evidence |
|---|---|---|
| B1. Shared Toolkit command, `ac-test-toolkit-bindings` | PASS | `WorkbenchViewModel.FocusAsync` generates the cancellable `AsyncRelayCommand`. `WorkbenchWindow` gives the menu, button, and primary and floating shortcuts that command instance. `Menu_toolbar_and_shortcut_share_one_eligible_focus_command` checks identity, eligibility changes and headless button dispatch. `Cancellation_and_stale_context_produce_correlated_outcomes_without_mutation` checks running state and cancellation. |
| B2. Direct SimpleMVC ownership and lifetime, `ac-test-simplemvc-views` | PASS | `WorkbenchViewModel` derives from `ObservableObject` and implements `IView : ISimpleView`. `WorkbenchController.Register`, `Resolve`, and `Unregister` use the package registration and the controller's live records. Tests check one subscription after repeat registration, surviving registration lookup after removal, and rejection of re-registering a closed instance. |
| B3. Typed query and mutation, `ac-test-cqrs-unit-of-work` | PASS | `WorkbenchDispatch.cs` registers the read and focus handlers once. `WorkbenchController.DispatchAsync` executes the shared unit of work on the dispatcher. The read handler returns a snapshot with no write or adapter call. Tests check genuine Dock focus, one dispatch for the headless button, and two writes with the same revision yielding `ok` and `stale_revision` with one increment. |
| B4. Origin correlation and stale delivery suppression, `ac-test-view-result-routing` | PASS | `WorkbenchController.DispatchToViewAsync` verifies the same live registration on Avalonia's dispatcher before `Receive`. `Completion_is_correlated_to_its_live_origin_even_if_focus_changes` checks one correlated UI-thread result, no result on the other registration, and suppression when the origin closes before execution or when the replacement unregisters between a successful focus and delivery. |
| B5. Foreign workspace registration | PASS | `WorkbenchController.Register` checks `WorkspaceId` before calling `AddOrUpdateView`. `Foreign_workspace_cannot_register_with_the_workbench_controller` verifies the exception, no package registration, no enablement, and unchanged revision. |
| B6. Completion after focusing or docking the second registered view, `ac-test-view-result-routing` | FAIL | `WorkbenchDispatchTests.cs`, `Completion_is_correlated_to_its_live_origin_even_if_focus_changes`, posts `session.Layout.Focus("output")` on the shared layout. The second `WorkbenchViewModel` is registered but the test never focuses or docks a surface belonging to it. The assertions prove routing across a pane selection change. |
| B7. Correlated failure delivered to the originating UI view, `ac-test-view-result-routing` | FAIL | In `WorkbenchDispatchTests.cs`, success and cancellation reach `LastResult`. Stale and revoked failures are asserted on direct `DispatchAsync` return values. The suite lacks an assertion that a failed command reaches the originating live ViewModel once with the same ID on the UI thread. |
| B8. Current Windows native toolbar dispatch | FAIL | The shared control path fails at the Windows native entry point in A4. |
| B9. Current Windows native shortcut and menu dispatch | UNKNOWN | `NativeDockingTests.InvokeWorkbenchBindingsAsync` reaches these only after toolbar success. Current Linux gets through all three with exactly one dispatch each. |

### 01C · FR-RETRO-015 / TR-RETRO-MCP-015 / TEST-RETRO-015

| Claim | Result | Source and evidence |
|---|---|---|
| C1. Phase 01 inventory, `ac-test-mcp-inventory` | PASS | `FeatureRegistry` defines the two supported tools. `LoopbackMcpHost.ConfigureTools` generates discovery and invocation lookup from that registry. `ViewModelContextContractTests` verifies the inventory and unique feature IDs. |
| C2. Public ViewModel schema and live context, `ac-test-mcp-viewmodel-context` | PASS | `ViewContextContract` derives schema and capture from the four `[McpContext]` properties and checks their types and nullability against `WorkbenchAddress`. `TryRead` enforces the exact key set. `Http_stdio_and_ui_focus_the_same_live_dockable` compares discovered input/output schemas and snapshots through both SDK clients and round-trips captured context. The unit test asserts the allowlist and the scope test rejects an extra field. |
| C3. Same instance and common focus path, `ac-test-mcp-same-instance` | PASS | `BridgeEntryPoint.Main` forwards SDK discovery and invocation to the descriptor endpoint. The managed test proves real Dock focus and seven total dispatches on the selected controller, zero on the other. Current Linux `ProductionAttachmentTests.RunAsync` proves two distinct desktop PIDs, selected-instance native shortcut/HTTP/stdio focus at revisions 2/4/6, rejection by the other endpoint, unchanged other context, and attachment loss plus descriptor cleanup at shutdown. |
| C4. Current Windows production two-desktop SDK run | UNKNOWN | `NativeDockingTests.Main` calls `ProductionAttachmentTests.RunAsync` only after the native fixture succeeds. The managed Windows pass exercises both SDK transports. The latest Windows native run never reached the production two-desktop portion. |
| C5. Actor grants, loopback, Host and Origin, `ac-test-mcp-scope` | PASS | `LoopbackMcpHost.StartAsync` and `AuthenticateAsync` bind IPv4 loopback and check source, host, applicable Origin, and the issued bearer grant. `Transport_and_live_scope_rejections_have_no_focus_effect` checks anonymous access, wrong Host, invalid-Origin authenticated POST, matching-Origin observer reads, and observer-write denial. |
| C6. Workspace, registration, target, and revision guards, `ac-test-mcp-scope` and `ac-test-mcp-viewmodel-context` | PASS | `WorkbenchUnitOfWork.Execute` checks workspace, live registration, granted view, revision, and captured active tool before the handler. The SDK scope test checks foreign workspace, another live view in the same workspace, unknown tool, stale revision with the current active tool, and closed registration. Focus and revision assertions constrain side effects. |
| C7. Revocation at the write boundary | PASS | `ActorGrant.Authorize` rechecks revocation under the grant lock. `Revoking_an_authenticated_HTTP_mutation_while_queued_prevents_focus` revokes after the real SDK call enters the queue seam. It checks `unauthorized`, the operation ID, unchanged revision, and retained Registers focus. |
| C8. Controller workspace admission | PASS | The B5 change constrains controller-owned registrations. The foreign-registration integration test and SDK workspace scope checks exercise distinct boundaries. |
| C9. Real SDK cancellation of a queued mutation | PASS | `Disconnecting_an_authenticated_HTTP_mutation_while_queued_prevents_focus` cancels the SDK call after the seam has the operation ID. It waits for server-token cancellation before releasing into the real controller, then checks correlated `cancelled` and unchanged address and focus. Current Linux names the passing case and records the audit outcome. The frozen Windows managed suite also passed. The deterministic queue is before production dispatcher submission. |
| C10. QBrain export-backed CI trace | PASS | The A6 verifier input and missing-criterion control cover this slice. |
| C11. Trace coverage of context rejection tests | FAIL | `eng/Verify-Phase01Trace.ps1`, `$evidence`, maps `Transport_and_live_scope_rejections_have_no_focus_effect` only to `ac-test-mcp-scope`. Its XML also declares `ac-test-mcp-viewmodel-context`, and its body supplies the closed, mismatched and undeclared-context assertions for that criterion. The compiled trace gate would accept removal of that second link. |

### 01D · Phase 01 criteria from the 001, 007 and 008 requirement sets

| Claim | Result | Source and evidence |
|---|---|---|
| D1. Accepted startup topology, `TEST-RETRO-001#verify-ac-behavior-desktop-2` | PASS | `DockLayout.Compatible`, `RestorePinned`, and `ShowFloatingLayouts` handle a main documents-only branch with main pins and the remaining tools floating. `Startup_restores_main_pins_when_remaining_tools_are_floating` checks startup, unpinning without a surviving owner, and fallback for an invalid edge. The earlier startup crash is covered by the red/green evidence. |
| D2. Save, reopen, and save-failure lifecycle, `verify-ac-behavior-desktop-2` | PASS | `LayoutStore.Save` uses a temporary file and replaces the destination after a cancellation check. `WorkbenchWindow.SaveLayoutAndCloseAsync` uses the scoped block. `ShellLifecycleTests` verifies restored document and tool selection, repeated close handling, and cleanup when a save fails. Current Linux also verifies native floating selection on reopen. |
| D3. Restore the exact surviving dynamic pin owner | PASS | `DockLayout.CaptureToolGroupIds`, `CaptureBranch`, and `RestorePinned` save and resolve unique group IDs. The five theory cases exercise blank IDs with Right and Unset alignment, duplicate, disallowed, and valid IDs. They assert persisted group identity, restored `OriginalOwner`, and actual owner after unpinning. |
| D4. Running adapter fixture survives layout restore, `verify-ac-behavior-desktop-2` and `verify-ac-contract-desktop-2` | PASS | `RunningAdapterFixture.OpenWorkbench` passes the same running fixture into `WorkbenchSession` before `DockLayout.CreateRoot` and `Factory.InitLayout`. The test advances the same session while the workbench is closed, observes unchanged adapter state through reopen, advances it again, and checks the effect sequence and exclusion of session/workspace IDs from the layout file. |
| D5. Dispatcher responsiveness and managed block lifecycle, `TEST-RETRO-007#ac-test-system-wait-cursor` | PASS | `InteractionBlocks.RunAsync` calls the delegate via `Task.Run`. The synchronous delegate tests hold the worker open while the dispatcher observes scoped and unscoped states. The managed overlap test verifies one remaining block retains Wait and the prior cursor returns after cancellation and failure. |
| D6. Linux native effective Wait and restoration | PASS | `NativeDockingTests.VerifyWaitAsync` compares the effective OS cursor against a platform Wait reference on docked and floating Registers. It verifies the blocked overlay, disabled content, restoration, and native input to the available picker in the main window. Current Linux output confirms both surfaces. |
| D7. Current Windows native Wait and floating restore | UNKNOWN | The current Win32 run stopped before both cursor checks and the native restore check. |
| D8. Required native wait lifecycle coverage | FAIL | `NativeDockingTests.VerifyWaitAsync` exercises one successful blocking delegate per surface. Overlap, an unscoped operation, failure, and cancellation appear only in managed fixtures. The native test has no rendered-frame assertion during the block. |
| D9. QBrain trace input in CI | PASS | The workflow and export-backed verifier invocation in A6 also cover this slice. |
| D10. Bundled command presentation, `TEST-RETRO-008#ac-test-gesture-parity` | PASS | `Resources/en.json`, `CommandPresentation.Create`, and `WorkbenchWindow` provide one label, icon geometry, and gesture for both controls. The managed test checks binding and presentation properties. Current Linux native input invokes the same command once from each entry point. |
| D11. Reject a conflicting binding in its activation scope | FAIL | `CommandPresentation.Create` calls `ValidateGestures([gesture])` with a singleton. `WorkbenchWindow.AddFocusShortcut` installs the binding without comparing it against bindings in that scope. The managed test proves duplicate detection for an artificial two-item helper input. The actual installation path has no conflict check. |
| D12. Trace coverage for gesture parity | FAIL | `eng/Verify-Phase01Trace.ps1`, `$evidence`, links `ac-test-gesture-parity` only to native `RunAsync`. That test exercises invocation and omits metadata and conflict assertions. The managed `Menu_toolbar_and_shortcut_share_one_eligible_focus_command` test declares this criterion and contains those assertions, but its compiled entry is checked only for `ac-test-toolkit-bindings`. |
| D13. Recovery when the last floating host holding a tool closes | FAIL | `DockLayout.RestoreLostPanes` can reinsert an orphan only into a main `IToolDock` with the tool's original default group ID. D1 explicitly accepts a main layout with no tool groups. In that topology, the orphan-recovery path has no destination or fallback for an orphaned Registers or Output pane. The effect of closing that native host was not exercised. |
| D14. Effective native cursor through the unexercised lifecycle transitions | UNKNOWN | The managed cursor property and dispatch assertions do not observe the OS cursor during overlap, background work, failure, or cancellation. Picker interaction establishes responsive input. The supplied captures leave continued rendering during the block unmeasured. |

## Negative controls and prior findings

| Control | Exact behavior established |
|---|---|
| Stale revision with current tool selection | Removing the revision check changes `stale_revision` to `ok` while the active-tool equality check remains in place. This isolates revision enforcement. |
| Wrong live view | Removing the granted-view comparison changes `view_mismatch` to `ok` for a second registered view in the **same** workspace. The separate foreign-registration red/green establishes B5/C8. |
| Queued HTTP revoke | Removing the revocation check changes `unauthorized` to `ok` for an already authenticated write waiting at the seam. |
| Cancel before write | Removing the unit-of-work check changes the Toolkit cancellation result from `cancelled` to `ok`. C9's SDK cancellation is established by its separate current passing run. |
| Duplicate subscription | Adding a second handler fails the invocation-list `Assert.Single()` with two entries. `request.Completion` can suppress a second dispatch, so this red output proves duplicate subscription specifically. |
| Origin on POST | Relaxing the Origin check changes the authenticated mutating POST from HTTP 403 to HTTP 200. |
| Trace | Removing `TEST-RETRO-015#ac-test-mcp-scope` makes the export-backed verifier reject the input. This proves criterion-ID enforcement for a checked link. |
| Startup, synchronous delegate and dynamic pin | The startup and delegate reds show failures on the named earlier fixtures. The pin reds expose missing saved IDs or a wrong restored owner. The current tests pass all five pin variants and both scoped and unscoped synchronous delegate cases. |

The earlier A6/C10/D9 missing-export input, B5/C8 foreign registration, D3 surviving pin owner, D4 fixture association, and C9 queued SDK cancellation findings are resolved within the tested boundaries above. Current Linux also resolves the earlier two-process evidence gap on that host. Shared in-flux requirements are **001** across 01A/01D for native shell and persistence, **007** across 01B/01D for shared command behavior and cursor eligibility, and **015** across 01B/01C for registration and typed dispatch. The Windows native entry failure affects all four slices.

## Out of scope

Each technical ID below denotes its linked `FR-RETRO-nnn / TR-RETRO-…-nnn / TEST-RETRO-nnn` set. Only the listed later-phase portions are out of scope.

| Phase | Plan requirement sets and later work |
|---|---|
| 02 | `TR-RETRO-COMMAND-007`, `TR-RETRO-LOCALIZATION-008`, `TR-RETRO-EDITOR-011`, `TR-RETRO-EXPLORER-012`, `TR-RETRO-MCP-015` for MDI, editor, project navigation, localization, and parity for those features. |
| 03 | `TR-RETRO-GIT-013`, `TR-RETRO-LSP-014`, `TR-RETRO-MCP-015` for Git, terminal, LSP, and their MCP parity. |
| 04 | `TR-RETRO-BUNDLE-009`, `TR-RETRO-ADAPTER-010`, `TR-RETRO-MCP-015` for emulator bundles, adapter conformance, and associated MCP coverage. |
| 05 | `TR-RETRO-DESKTOP-001`, `TR-RETRO-PROFILE-002`, `TR-RETRO-COMMAND-007`, `TR-RETRO-BUNDLE-009`, `TR-RETRO-ADAPTER-010`, `TR-RETRO-MCP-015` for real-provider launch, build/debug, emulator reattachment, capability and provider evidence. |
| 06 | `TR-RETRO-DEBUG-004`, `TR-RETRO-ADAPTER-010`, `TR-RETRO-EDITOR-011`, `TR-RETRO-MCP-015` for debugger, consoles, symbols, and breakpoint navigation. |
| 07 | `TR-RETRO-AVINPUT-003`, `TR-RETRO-WORKFLOW-005`, `TR-RETRO-ADAPTER-010`, `TR-RETRO-MCP-015` for player, media, snapshots, tests, live ports, and operation handling. |
| 08 | `TR-RETRO-DESKTOP-001`, `TR-RETRO-AGENT-006`, `TR-RETRO-MCP-015` for all scoped panes, agent reconnect and grants, full registry and transport parity, and release evidence. |
