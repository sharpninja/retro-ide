# Testing Requirements (QBrain.AI)

## TEST-RETRO

### TEST-RETRO-001

Verify each FR-RETRO-001 and TR-RETRO-DESKTOP-001 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-001#ac-behavior-desktop-1. Dock, tab, drag out into a separate window, and redock a supported tool pane on each target host.
- [ ] Verify FR-RETRO-001#ac-behavior-desktop-2. Save and restore a layout across desktop restarts while the emulator session remains independently accessible.
- [ ] Verify FR-RETRO-001#ac-behavior-desktop-3. Open supported Emulation, console, logs, debugger, snapshot, test, and agent activity views scoped to the selected instance.
- [ ] Verify TR-RETRO-DESKTOP-001#ac-contract-desktop-1. Run the desktop on Windows and Linux without requiring the adapter host to load desktop dependencies.
- [ ] Verify TR-RETRO-DESKTOP-001#ac-contract-desktop-2. Restore the docking layout without altering emulator instance state.
- [ ] Verify TR-RETRO-DESKTOP-001#ac-contract-desktop-3. Reattach to a running instance through the adapter.

### TEST-RETRO-002

Verify each FR-RETRO-002 and TR-RETRO-PROFILE-002 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-002#ac-target-presets. The same IDE offers UAE, VICE, and ViceSharp presets and accepts an additional adapter without converting the built-in presets into special cases.
- [ ] Verify FR-RETRO-002#ac-build-launch. A failed build exposes a source diagnostic in the IDE and starts no emulator. A successful debug launch opens Emulation for the selected target.
- [ ] Verify FR-RETRO-002#ac-session-controls. Display the selected instance and lifecycle. Surface isolated faults, in-progress operations, stale revisions, and loss of control without applying an unauthorized mutation.
- [ ] Verify TR-RETRO-PROFILE-002#ac-profile-contract. Validate the shared preset shape independently of adapter-specific payload validation. Keep binaries, ROMs, and endpoint settings external.
- [ ] Verify TR-RETRO-PROFILE-002#ac-capability-negotiation. Distinguish deliberate capability unavailability from adapter connection or launch failures.

### TEST-RETRO-003

Verify each FR-RETRO-003 and TR-RETRO-AVINPUT-003 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-003#ac-render-capture. Render and capture a known fixture frame for the selected instance.
- [ ] Verify FR-RETRO-003#ac-audio-input. Play selected guest audio and route keyboard and pointer input to the selected guest according to the adapter’s capabilities.
- [ ] Verify FR-RETRO-003#ac-responsive-media. Keep controls responsive under video and audio load and display frame-delivery and audio-underrun observations where available.
- [ ] Verify FR-RETRO-003#ac-seek-volume. In a seekable recording, drag the playhead on the progress bar under the time marker and update the displayed timestamp and frame. Set volume independently. For live emulation show elapsed time and disable unavailable seeking.
- [ ] Verify TR-RETRO-AVINPUT-003#ac-media-presentation. Use adapter-described dimensions, format, timing, and input routing. Prevent media, input, and playback state from bleeding between instances.

### TEST-RETRO-004

Verify each FR-RETRO-004 and TR-RETRO-DEBUG-004 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-004#ac-debug-workflow. Pause, step, and continue through a fixture and show the current location, registers, memory, and breakpoint state for the selected target.
- [ ] Verify FR-RETRO-004#ac-symbol-fallback. Resolve symbols and source when valid for the selected build. On missing or mismatched symbols retain raw address and disassembly views.
- [ ] Verify FR-RETRO-004#ac-console-sources. Expose supported console input, early boot output, guest logs, and host events with distinct sources.
- [ ] Verify FR-RETRO-004#ac-guest-availability. Expose guest-specific inspectors when advertised and describe unavailable capabilities while keeping supported machine-level views usable.
- [ ] Verify TR-RETRO-DEBUG-004#ac-machine-neutral. The common shell does not assume 68040, AGA, 32-bit addresses, ELF/DWARF, or an A1200 OS guest.
- [ ] Verify TR-RETRO-DEBUG-004#ac-observation-scoping. Tag observed state by target and instance. Display raw state safely when an optional interpreter or guest extension is unavailable.

### TEST-RETRO-005

Verify each FR-RETRO-005 and TR-RETRO-WORKFLOW-005 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-005#ac-media-confirmation. Show attached media for the selected instance and prompt before a destructive operation where policy requires it.
- [ ] Verify FR-RETRO-005#ac-snapshot-flow. Show available snapshots, compatibility, compare and restore where supported. Never offer a failed or incomplete snapshot for restore.
- [ ] Verify FR-RETRO-005#ac-test-diagnostics. Run a supported test from the IDE, show assertions and failure details, and open exported diagnostic evidence or a supported replay without losing the session context.
- [ ] Verify TR-RETRO-WORKFLOW-005#ac-artifact-contract. Route mutations through authorized instance operations and carry provenance and operation state into the corresponding panels.

### TEST-RETRO-006

Verify each FR-RETRO-006 and TR-RETRO-AGENT-006 criterion against controlled adapter fixtures. Run desktop checks on Windows and Linux, and report backend integration separately.

**Acceptance Criteria:**
- [ ] Verify FR-RETRO-006#ac-agent-connect. From the desktop, connect a configured agent and display reads, authorized actions, and denials for the selected instance.
- [ ] Verify FR-RETRO-006#ac-agent-reconnect. On agent or service disconnection, explain the failure and offer reattachment when available. Agent loss does not end a running emulator session.
- [ ] Verify TR-RETRO-AGENT-006#ac-agent-authorization. Agent activity respects host capabilities and permissions and does not introduce a path around the adapter.

### TEST-RETRO-007

Verify FR-RETRO-007 and TR-RETRO-COMMAND-007 with SimpleMVC, Unit of Work, build, and adapter fixtures. Verify menu and command-bar bindings on Windows and Linux.

**Acceptance Criteria:**
- [ ] Invoke Build Project separately through a menu and command bar, using the same fixture context. Each invokes the same registered controller route exactly once, reports the same diagnostics, and does not launch an emulator.
- [ ] Change target capability and operation state. Both controls update availability and an attempted disabled or stale execution causes no build or emulator mutation.
- [ ] Run Start Debugging with a successful build and a failed build. Only the successful handler launches the captured target. Both outcomes reach the ViewModel through the observable Model.
- [ ] Verify every IDE ViewModel type implements IView and is assignable to the package ISimpleView. Register two live instances with distinct keys and remove one without removing the other.
- [ ] Prove a typed request is dispatched once by the main controller into the expected Unit of Work handler. Confirm the query fixture produces no write or emulator mutation. Confirm command success, failure, and stale-context rejection produce the correct Model outcome.
- [ ] Verify ViewModels implement IView and notify through ObservableObject. Bind menu, command bar, and shortcut to one Toolkit command. Verify eligibility notifications, one subscribed dispatch, running state, and cancellation propagation.
- [ ] Register two views and complete a request from one after focusing or docking the other. Assert one correlated completion on the originating Avalonia UI thread. Close or replace a view before delivery and assert there is no stale callback. Exercise success, failure, and cancellation.
- [ ] Register a workbench view with the main controller and two editor views with the document controller. Assert each command event is handled once by its registered owner and the shared CQRS entry point receives one typed request. Invoke Save in the first editor, activate the second while the save is pending, and assert completion updates the first on the Avalonia UI thread. Unregister the first editor and verify a late callback is suppressed.
- [ ] On Windows and Linux, start a logically blocking operation and verify the platform wait cursor over the blocked scope while paint and available controls keep working. Check docked and floating views, a nonblocking background task, two overlapping blocks, and cursor restoration after success, failure, and cancellation.

### TEST-RETRO-008

Verify FR-RETRO-008 and TR-RETRO-LOCALIZATION-008 across Windows and Linux using exact, parent, default, right-to-left, and expanded pseudo-localized resource fixtures.

**Acceptance Criteria:**
- [ ] Compare menu and command bar labels, icons, and shortcut hints. Invoke a command from the keyboard and verify the same command runs once with the same availability. Reject a conflicting binding in an overlapping activation scope.
- [ ] Resolve a text slug and image slug from exact, parent, and default catalogs, including a locale that has a translated label but reuses the default icon. A missing or mistyped entry produces the specified fallback and diagnostic.
- [ ] Reject malformed patterns, incompatible message parameters, invalid resource types, and image paths outside their pack. Verify the bundled catalog covers all required slugs.
- [ ] Change the language through the IDE and reopen it to verify persistence. Verify open windows refresh strings, localized icons, and accessible names, honor right-to-left flow, handle expanded pseudo-localized labels, and retain keyboard bindings.
- [ ] Exercise named arguments, plural and select forms, and locale-specific number and date formatting, including a message resolved through fallback.

### TEST-RETRO-009

Verify the Emulators manager and package resolution using fixture Git buckets and archives for Windows and Linux.

**Acceptance Criteria:**
- [ ] Refresh a fixture Git bucket and install the correct host archive. Check manifest revision, entry points, receipt, and clear diagnostics for incompatible platform and adapter contract.
- [ ] Exercise a digest mismatch, an invalid archive path, a missing entry point, and an interrupted install. Confirm existing versions and the last valid catalog remain usable.
- [ ] Pin a project, install a newer release alongside it, launch the pinned version offline, and select a prior verified release. Verify a live session blocks removal of its installed version.

### TEST-RETRO-010

Run a shared conformance fixture and exercise the actual supported adapter capabilities for UAE, VICE, and ViceSharp on supported hosts.

**Acceptance Criteria:**
- [ ] Use the fixture to verify launch and attach, incompatible versions, capability changes, unsupported features, disconnect, reconnect identity, and detach or close behavior.
- [ ] Verify correlated results, mutation revision conflicts, unknown outcome reconciliation, and state reload after an event gap.
- [ ] Flood a supported fixture media stream while issuing status and control requests. Verify bounded streams, discontinuity signaling where applicable, and independent control progress.
- [ ] Exercise at least one operation per advertised capability on each shipped backend, and confirm an older client handles a fixture exposing a new optional port.

### TEST-RETRO-011

Verify AvaloniaEdit, its tab lifecycle and controller routing, and source navigation on Windows and Linux.

**Acceptance Criteria:**
- [ ] Edit two files, select and navigate within each, undo and redo independently, search and replace, and float and redock a tab while retaining its state.
- [ ] Verify successful and failed saves and edits made during a pending save. Open a source diagnostic and resolve its line and column.
- [ ] Open a mapped debugger stop, exercise a supported gutter breakpoint request, and confirm the reported marker. Verify raw debugging remains available for an unmapped source location.
