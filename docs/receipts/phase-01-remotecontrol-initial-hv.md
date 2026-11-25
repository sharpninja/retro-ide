# Phase 01 first RemoteControl validation

This read-only run on LEGION2 reviewed the first input-free candidate. The full [request](hv/phase-01-remotecontrol-initial/request.jsonl) and [response](hv/phase-01-remotecontrol-initial/response.jsonl) accompany this result.

# Retro IDE Phase 01 hostile-validation receipt

OverallVerdict: DISAGREE

OverallAccuracy: 97

OverallCompleteness: 92

ValidatorIdentity: ChatGPT-6.2-SOL-HIGH-LEGION2

Task 01A EXEC-TODO-001: DISAGREE (accuracy 98, completeness 92)

Task 01B EXEC-TODO-002: DISAGREE (accuracy 96, completeness 92)

Task 01C EXEC-TODO-003: DISAGREE (accuracy 98, completeness 95)

Task 01D EXEC-TODO-004: DISAGREE (accuracy 94, completeness 88)

All four tasks are open and in flux. There are no completed or regression-only tasks this round. Scores are validator judgments of correctness and acceptance coverage. Overall scores are the rounded means of the four task scores. Each AGREE requires at least 98 on both measures.

## Execution evidence

The separate LEGION2 host harness reported that `sha256sum -c` passed before and after the fresh Windows scratch build and managed tests. It used .NET SDK `10.0.404` and reported:

| Harness command | Captured result |
| --- | --- |
| `dotnet build RetroIde.sln -c Release --disable-build-servers` | `Build succeeded. 54 Warning(s), 0 Error(s)` |
| `dotnet test RetroIde.sln -c Release --no-build` | Unit 2 passed, 0 failed, 0 skipped. Integration 19 passed, 0 failed, 0 skipped. |

The independent excerpt supplies command outcomes without numerical shell exit codes or warning IDs. I reviewed the supplied snapshot and receipts. I did not launch source inspection, builds, tests, or desktop input.

- The earlier managed Linux and Windows records identify archive `c314cc470f34d2f05686b500e2edf7452162e0f5f7c08f2ea02f0ec2932e351a`. The current Linux native record shows two passing X11/XTEST runs. Current Windows evidence is the input-free managed suite.
- The signed-in Windows desktop evidence came from earlier native runs outside the independent harness. The earlier `HWND`/Win32 run passed docking and effective cursor checks. The later pre-RemoteControl run timed out at the toolbar with the window active, button hovered, zero dispatches, and revision zero. The supplied Windows two-desktop parity and shutdown record is also historical. Current Windows native behavior remains unknown.
- `.github/workflows/phase01.yml` configures Windows and Linux build, managed test, and QBrain trace gates. Linux native smoke runs on Xvfb. Windows physical-input smoke is opt-in as directed. The receipts establish local executions and workflow configuration. An independently executed public hosted job is outside the acceptance gate.
- The supplied packet includes rule 6 and the operator’s explicit adjudication rules. The full `MEMORY-HV-100` record was unavailable through the exposed tools. I applied the supplied rules and scored the evidence on its merits. The earlier socket-denied run does not affect verdicts where the current Linux or independent Windows evidence establishes the result.

## Requirements matrix

| Task | In-flux Phase 01 requirement set | Shared requirements |
| --- | --- | --- |
| 01A | `FR-RETRO-001` / `TR-RETRO-DESKTOP-001` / `TEST-RETRO-001` | 001 layout and native shell with 01D. Build and trace infrastructure with 01B, 01C, and 01D. |
| 01B | `FR-RETRO-007` / `TR-RETRO-COMMAND-007` / `TEST-RETRO-007` | Typed dispatch and live registration with 01C. Toolkit eligibility and command presentation with 01D. |
| 01C | `FR-RETRO-015` / `TR-RETRO-MCP-015` / `TEST-RETRO-015` | Controller ownership, workspace fence and live revision with 01B. Compiled trace with 01A and 01D. |
| 01D | Phase 01 criteria of 001 and 007, plus `FR-RETRO-008` / `TR-RETRO-LOCALIZATION-008` / `TEST-RETRO-008` | Native panes and persistence with 01A. Command identity and scoped eligibility with 01B. |

**FAIL** identifies a source defect or a demonstrable gap in a required test or gate. **UNKNOWN** identifies missing evidence for a specific behavior.

### 01A · Native workbench shell

| Criterion and location | Outcome | Evidence and limits |
| --- | --- | --- |
| `TEST-RETRO-001#verify-ac-behavior-desktop-1` · `Program.cs`, `WorkbenchApp.Initialize`, `WorkbenchWindow.BuildSurface`, `DockLayout.CreateRoot`, `NativeDockingTests.DragToolAsync` | **PASS Linux. UNKNOWN current Windows native.** | The app uses platform detection, the Avalonia desktop lifetime, a real `DockControl`, and native Dock host windows. Current Linux mouse input selected a tab, tore a tool out, verified a distinct native handle, and redocked it into the original group. The historical Windows pass establishes that behavior for its revision. Current Windows RemoteControl proves rendered control invocation on Headless and supplies no native drag or handle observation. |
| `TEST-RETRO-001#verify-ac-contract-desktop-1` · `RetroIde.sln`, production project files, `ProductionAttachmentTests.RunAsync`, `ShellLifecycleTests.RunningAdapterFixture` | **PASS dependency direction and current Linux desktop process. UNKNOWN current Windows desktop process.** | Avalonia and Dock dependencies live in the workbench project. Application, MCP and stdio project references do not pull in the desktop project. No production adapter host ships in this slice. The current Linux native run launches the executable desktop. The current Windows build and Headless test run establish managed operation. Its native executable evidence is historical. |
| Release, analyzer, and trace gates · `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.Config`, `.github/workflows/phase01.yml`, `eng/Verify-Phase01Trace.ps1` | **PASS build, test, and analyzer configuration. FAIL trace coverage.** | The solution builds and the full managed suite passes on both hosts as recorded. Central `GlobalPackageReference` applies Meziantou Analyzer, XML docs are enabled, and the reported SDK is allowed by the pinned patch roll-forward. The shared trace defect is detailed below. The warning breakdown is unavailable. |

### 01B · Shared command and controller

| Criterion and location | Outcome | Evidence and limits |
| --- | --- | --- |
| `TEST-RETRO-007#ac-test-simplemvc-views` · `WorkbenchViewModel.cs`, `WorkbenchController.Register`, `Resolve`, `Unregister`, `WorkbenchDispatchTests` | **PASS.** | The command-participating ViewModel inherits `ObservableObject` and implements `IView : ISimpleView`. One `SimpleControllerBase` owner registers the actual instance, resolves it against the package registry, subscribes once, and revokes and detaches on removal. Two same-workspace keys coexist. Removing one leaves the other available. The foreign-workspace test proves rejection before registration or command eligibility changes. |
| `ac-test-toolkit-bindings` · `WorkbenchWindow` constructor, `WorkbenchViewModel.FocusAsync`, `WorkbenchRemoteControl.ClickAsync`, `WorkbenchDispatchTests.Menu_toolbar_and_shortcut_share_one_eligible_focus_command` | **PASS shared binding, eligibility, running state, and cancellation.** | The rendered toolbar and menu resolve by automation ID and invoke the same `AsyncRelayCommand`. The Headless key event takes that command’s shortcut path. The test checks one subscriber and dispatch counts of one, two, and three. `Cancellation_and_stale_context_produce_correlated_outcomes_without_mutation` checks Toolkit running state, cancellation, correlation, and unchanged revision. RemoteControl Runtime is referenced by the integration project only. |
| `ac-test-cqrs-unit-of-work` · `WorkbenchDispatch.cs`, `WorkbenchController.DispatchAsync`, `McpWorkbenchTransportTests.Http_stdio_and_ui_focus_the_same_live_dockable` | **PASS Phase 01 typed dispatch.** | UI and MCP focus use the same `Feature<FocusToolInput>` and UI-dispatched Unit of Work against the actual Dock target. A same-revision race permits one focus write and rejects the second request. The query handler reads and captures the live context without a write or adapter call. The managed observer-read and rejection sequence leaves revision zero. |
| `ac-test-view-result-routing` · `WorkbenchController.DispatchToViewAsync`, `WorkbenchViewModel.Update` and `Address`, `WorkbenchDispatchTests.Completion_is_correlated_to_its_live_origin_even_if_focus_changes` | **PASS tested delivery and disposal. FAIL full routing coverage and observable context consistency.** | The test proves one correlated success on the originating UI thread after Dock selection moves to Output. It also suppresses delivery when the origin is removed before execution and when the replacement is removed after a successful write. The second ViewModel is only registered. The test focuses another tool in the shared layout, so it never focuses or docks the second registered view. There is no correlated ordinary UI failure assertion with both views live. Separately, `Update` publishes `WorkbenchRevision` before updating `ActiveToolId`. A synchronous revision observer can read `Address` with the new revision and old tool, a combination the Unit of Work will reject as `context_mismatch`. |

### 01C · Live MCP context and transport

| Criterion and location | Outcome | Evidence and limits |
| --- | --- | --- |
| `TEST-RETRO-015#ac-test-mcp-inventory` · `FeatureRegistry`, `LoopbackMcpHost.ConfigureTools`, `ViewModelContextContractTests` | **PASS Phase 01 inventory.** | The current registry has exactly context read and tool focus. Both SDK clients discover both names and identical input and output schemas. HTTP discovery and dispatch both enumerate the same typed registrations. |
| `ac-test-mcp-same-instance` · `BridgeEntryPoint.Main`, `WorkbenchApp.StartMcpAsync` and `StopMcpAsync`, `ProductionAttachmentTests.RunAsync`, `McpWorkbenchTransportTests.Http_stdio_and_ui_focus_the_same_live_dockable` | **PASS current managed and Linux production parity. UNKNOWN current Windows two-desktop native parity.** | The managed test uses the rendered toolbar, direct Streamable HTTP and a real stdio bridge process. Each route restores Registers from Output with one revision increment. The selected controller records seven operations and the other records zero. Current Linux uses two actual desktop PIDs and distinct endpoints. Both clients see the selected workspace, reject the other workspace, and observe revisions 2, 4 and 6. After selected-desktop shutdown both clients lose access, the other desktop remains live, and both descriptors are removed on graceful exit. The bridge attaches to the selected endpoint and has no desktop launch path. |
| `ac-test-mcp-viewmodel-context` · `ViewContextContract.cs`, `WorkbenchViewModel.cs`, `McpWorkbenchTransportTests` | **PASS live projection and round trip. FAIL schema constraint parity. UNKNOWN stdio stale and closed-registration coverage.** | Explicit metadata drives a closed four-field schema and dispatcher-captured snapshots. The typed contract checks name, type, and nullability parity. Both clients round-trip live context. The HTTP test proves rejection of a complete context with an extra member, a live same-workspace wrong view, a stale revision, and a closed registration. The published context schema is generated from the CLR types and does not express the nonnegative revision or nonempty identity constraints enforced by `TryRead`. The stdio tests do not exercise stale or unregistered context while the host remains live. Whole-desktop shutdown exercises a different boundary. |
| `ac-test-mcp-scope` · `LoopbackMcpHost.AuthenticateAsync`, `AttachmentDescriptor.CreatePrivateFile`, `ActorGrant.Authorize`, `WorkbenchUnitOfWork.Execute` | **PASS tested Phase 01 authorization and no-effect guards.** | Tests challenge anonymous access, wrong endpoint and Host, a real mutation POST with invalid Origin, read-only mutation, foreign workspace, wrong live view, stale revision, closed registration, and a revoked grant. The write boundary rechecks the actor, revision, and active selection. The owner-only descriptor is configured on both platforms. Linux mode is asserted and Windows descriptor creation and use are exercised by the managed suite. Effective Windows ACL inspection is absent. |
| Queued mutation cancellation and revoke · `McpWorkbenchTransportTests.QueuedMutation` and its two tests | **PASS real authenticated HTTP SDK request at the dispatch seam.** | The cancellation test waits until the server-side request enters the seam, cancels the SDK call, observes the server token, then releases the request into the real controller. It asserts the same operation ID, `cancelled`, unchanged address and unchanged Dock focus. Current Linux also records the cancellation audit. The revoke test holds the same seam until after revoke and asserts correlated `unauthorized` with unchanged state. The seam holds the operation immediately before posting to the production dispatcher. These tests establish that boundary and do not exercise an already-enqueued Avalonia callback or an in-flight stdio disconnect. |

### 01D · Layout, wait lifecycle, and catalog

| Criterion and location | Outcome | Evidence and limits |
| --- | --- | --- |
| `TEST-RETRO-001#verify-ac-behavior-desktop-2` and `#verify-ac-contract-desktop-2` · `LayoutStore`, `DockLayout.CaptureBranch`, `CaptureToolGroupIds`, `RestorePinned`, `WorkbenchSession`, `ShellLifecycleTests` | **PASS tested layout and running-fixture restore. UNKNOWN process-restart restore.** | The fixture supplies the same workspace context before both Dock initializations. The test closes and disposes the first workbench, advances the fixture while closed, restores Emulation selection, Registers placement and focus, and a pinned Call Stack, and advances the same fixture again. It checks the exact effects sequence and verifies that the persisted JSON excludes session ID, workspace ID, and the test path. Five dynamic-owner round trips cover blank, duplicate, disallowed and valid saved IDs plus unset alignment. The prior accepted-topology startup crash and the layout write-failure close path have passing managed tests. The native test restores a floating layout in a second window in the same process. The production attachment test verifies the layout file at exit. There is no captured production process restart reading that saved layout. |
| Recovering orphaned panes · `DockLayout.RestoreLostPanes`, `Compatible`, `CaptureBranch` | **FAIL recovery fallback. UNKNOWN native reachability of the orphan case.** | The recovery callback only adds a lost pane if its fixed default group ID still exists in the main root. `Compatible` accepts a document-only main root with tools in floating branches, and dynamic groups can have different IDs. If Dock leaves a tool orphaned when such a floating host closes, the callback has no destination fallback. The supplied tests do not close that accepted floating topology and verify all tools remain capturable. |
| `TEST-RETRO-007#ac-test-system-wait-cursor` · `InteractionBlocks.RunAsync`, `Wrap`, `SurfaceSubscription.Update`, `NativeDockingTests.VerifyWaitAsync`, `NativeInput.ReadCursor`, `ShellLifecycleTests` | **PASS native Linux success path and managed lifecycle. UNKNOWN full native cursor lifecycle.** | Current Linux checks the effective XFixes cursor image against a standard Wait reference over docked and floating Registers, then checks normal cursor restoration and re-enabled content. It also operates the available tool picker while Registers is blocked. The earlier Windows pass sampled the effective Win32 cursor handle. Headless tests prove that synchronous delegates yield the dispatcher, overlapping blocks retain the override, unrelated surfaces retain their cursor, and failure and cancellation restore it. Native testing checks a single successful block. There is no current Windows effective-cursor observation or native observation for overlapping blocks, a nonblocking background task, failure, or cancellation. Responsive native input is observed on Linux. Paint completion is not measured directly. |
| `TEST-RETRO-008#ac-test-gesture-parity` · `Resources/en.json`, `CommandPresentation.Create` and `ValidateGestures`, `WorkbenchWindow.AddFocusShortcut`, `WorkbenchDispatchTests.Menu_toolbar_and_shortcut_share_one_eligible_focus_command` | **PASS bundled presentation and enabled invocation parity. FAIL production conflict-proof coverage.** | Label, icon geometry, and gesture come from the same descriptor and bundled English catalog. Both rendered controls and the shortcut reach the one command. The test rejects a duplicate by calling the helper directly. Production passes only `[gesture]` to that helper, so the assertion does not prove rejection when bindings are installed in an overlapping scope. The managed test checks disabled menu and toolbar controls and invokes the shortcut only after eligibility is restored. There is no disabled-shortcut no-dispatch assertion. |

## Shared trace and negative controls

**A6 / C10 / D9.** The previous failure to pass QBrain input to CI is resolved in workflow source. The recorded command `pwsh -NoProfile -NonInteractive -File eng/Verify-Phase01Trace.ps1 -QBrainExport docs/requirements/phase-01-test-criteria.yaml` reported **28 test links, nine public contracts, and 14 criterion IDs** for the compact projection (`AA2A03B4…DC677`). The supplied full effective export produced the same counts (`E3A9759A…2E181`). Removing `TEST-RETRO-015#ac-test-mcp-scope` from the input was rejected.

**Remaining trace FAIL.** `Verify-Phase01Trace.ps1` checks the hard-coded `$evidence` list against compiled XML and the supplied QBrain input. It does not check the reverse mapping. Three annotated links absent from its table are `Transport_and_live_scope_rejections_have_no_focus_effect` to ViewModel context, `Cancellation_and_stale_context_produce_correlated_outcomes_without_mutation` to Toolkit bindings, and `Close_finishes_and_clears_its_block_when_layout_save_fails` to the wait-cursor criterion. Public boundaries including `IWorkbenchDispatcher`, `IView`, and `AttachmentDescriptor` are also outside the nine-contract check. The missing-criterion control proves enforcement for a listed ID. It supplies no coverage for omissions from the list itself.

| Supplied control | Exact behavior established |
| --- | --- |
| Stale revision, same active tool | With the revision guard removed, the test returned `ok` instead of `stale_revision`. The payload preserves the current tool selection, isolating the revision check. |
| Wrong live view | With the actor-to-live-view key check removed, another registered view in the same workspace returned `ok` instead of `view_mismatch`. This isolates view scope from workspace scope. |
| Queued HTTP revoke | With the write-boundary revoke guard removed, the authenticated request returned `ok` instead of `unauthorized`. This challenges authorization after middleware admission. |
| Cancellation before write | Removing the cancellation check produced `ok` instead of `cancelled` in the Toolkit/controller test. The current SDK cancellation test separately proves HTTP cancellation reaches that guard. |
| Duplicate subscription | Re-registering with a second subscription produced `Assert.Single()` with two items. The control directly measures the subscription invariant. The ordinary test separately measures dispatch counts. |
| Origin on POST | Relaxing the Origin guard caused the authenticated mutation POST to return HTTP 200 instead of 403. The restored guard passed the focused test. |
| Startup, dispatcher and follow-up red controls | Prior runs show the accepted topology’s `Sequence contains no elements`, two inline-delegate dispatcher timeouts, a lost dynamic owner, and failure to reject a foreign-workspace registration. The current suites pass the corresponding cases. The receipts do not establish a full BDPv4 tests-first and mock-green execution history. |

The specific B5/C8 registration defect, D3 surviving-owner defect, and D4 fixture assignment timing are addressed. C9 is addressed for an actual authenticated HTTP SDK request held at the pre-dispatch seam. The earlier lack of real two-desktop SDK evidence is resolved for the current Linux candidate. Current Windows native acceptance, the routing and cursor cases above, and the remaining trace omissions keep the phase open.

## Out of scope · Phases 02 through 08

Each numbered set below denotes `FR-RETRO-NNN`, the specified technical requirement, and `TEST-RETRO-NNN`. The repeated numbers identify later slices of shared sets.

| Phase | Plan requirement sets outside this validation |
| --- | --- |
| 02 | `012 / TR-RETRO-EXPLORER-012`, `011 / TR-RETRO-EDITOR-011`, `008 / TR-RETRO-LOCALIZATION-008`, `007 / TR-RETRO-COMMAND-007`, and `015 / TR-RETRO-MCP-015` for mixed roots, editor and MDI, full localization, and added tool parity. |
| 03 | `013 / TR-RETRO-GIT-013`, `014 / TR-RETRO-LSP-014`, and `015 / TR-RETRO-MCP-015` for Git, terminal, LSP, and their scoped tools. |
| 04 | `009 / TR-RETRO-BUNDLE-009`, `010 / TR-RETRO-ADAPTER-010`, and `015 / TR-RETRO-MCP-015` for bundles, adapter conformance, and their tools. |
| 05 | `001 / TR-RETRO-DESKTOP-001`, `002 / TR-RETRO-PROFILE-002`, `007 / TR-RETRO-COMMAND-007`, `009 / TR-RETRO-BUNDLE-009`, `010 / TR-RETRO-ADAPTER-010`, and `015 / TR-RETRO-MCP-015` for build/debug, presets, provider evidence, production reattach, and guarded backend calls. |
| 06 | `004 / TR-RETRO-DEBUG-004`, `010 / TR-RETRO-ADAPTER-010`, `011 / TR-RETRO-EDITOR-011`, and `015 / TR-RETRO-MCP-015` for debugger, consoles, symbols, breakpoint navigation, and live tool coverage. |
| 07 | `003 / TR-RETRO-AVINPUT-003`, `005 / TR-RETRO-WORKFLOW-005`, `010 / TR-RETRO-ADAPTER-010`, and `015 / TR-RETRO-MCP-015` for playback and capture, media, snapshots, deterministic tests, live ports, and operation handling. |
| 08 | `001 / TR-RETRO-DESKTOP-001`, `006 / TR-RETRO-AGENT-006`, and `015 / TR-RETRO-MCP-015` for all scoped panes, agent attachment and reconnect, full inventory and transport parity, and release acceptance. |
