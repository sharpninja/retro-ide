# Retro IDE wireframes and storyboards

Source: the Retro IDE architecture and QBrain layer `layer-1`. UAE40 is the example for the inherited workflows. STB16 shows how the same IDE shell loads VICE and ViceSharp target presets.

IDE chrome reference: [Visual Studio window layouts and tabs](https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio). The frames use UAE Developer's own colors and show distinct document tabs, docked tool panes, and a persisted debug layout. The default pane arrangement and build integration are design proposals.

### Storyboard: STB01 Arrange tool windows and restore an IDE workspace {#stb01-restore-workspace}

- `frames/STB01/01-empty-workspace.svg` {#stb01-open}
  - Next: stb01-selected
- `frames/STB01/02-instance-selected.svg` {#stb01-selected}
  - Next: stb01-docked
- `frames/STB01/03-docked-tools.svg` {#stb01-docked}
  - Branch: Float Registers from its title bar -> stb01-floating
  - Branch: Auto-hide Registers with the pin control -> stb01-auto-hide
- `frames/STB01/04-floating-debugger.svg` {#stb01-floating}
  - Branch: Drag Registers over the right docking guide -> stb01-guide
- `frames/STB01/06-registers-auto-hidden.svg` {#stb01-auto-hide}
  - Loop: Reveal Registers from the edge and pin it again -> stb01-docked
- `frames/STB01/07-registers-docking-guide.svg` {#stb01-guide}
  - Branch: Drop to dock Registers on the right -> stb01-redocked
  - Loop: Cancel drag and keep Registers floating -> stb01-floating
- `frames/STB01/08-registers-redocked.svg` {#stb01-redocked}
  - Next: stb01-restored
- `frames/STB01/05-layout-restored.svg` {#stb01-restored}

<!-- Traceability: FR-UAE-001#ac-behavior-desktop-1, FR-UAE-001#ac-behavior-desktop-2, FR-UAE-001#ac-behavior-desktop-3, TEST-UAE-001. -->
<!-- Decision needed: Approve the exact default pane set and initial Dock.Avalonia layout. The architecture lists supported tools but does not define their first-run positions. -->

### Storyboard: STB02 Validate and boot a UAE40 machine {#stb02-validate-and-boot}

- `frames/STB02/01-profile-draft.svg` {#stb02-profile}
  - Branch: Validation succeeds -> stb02-valid
  - Branch: Mandatory capability is missing -> stb02-invalid
- `frames/STB02/02-profile-valid.svg` {#stb02-valid}
  - Next: stb02-starting
- `frames/STB02/03-starting.svg` {#stb02-starting}
  - Branch: Known serial marker is observed -> stb02-running
  - Branch: Worker exits during boot -> stb02-fault
- `frames/STB02/04-running-marker.svg` {#stb02-running}
- `frames/STB02/05-profile-invalid.svg` {#stb02-invalid}
  - Loop: Correct the profile and validate again -> stb02-profile
- `frames/STB02/06-worker-fault.svg` {#stb02-fault}
  - Loop: Retry boot with retained diagnostics -> stb02-starting

<!-- Traceability: FR-UAE-002#ac-behavior-profile-1, FR-UAE-002#ac-behavior-profile-2, FR-UAE-002#ac-behavior-profile-3, FR-UAE-003#ac-behavior-lifecycle-1, TEST-UAE-002, TEST-UAE-003. -->

### Storyboard: STB03 Isolate workers and reconnect the desktop {#stb03-isolate-and-reconnect}

- `frames/STB03/01-two-instances.svg` {#stb03-two}
  - Branch: Force the test worker to exit -> stb03-fault
  - Branch: Close the desktop while workers run -> stb03-closed
- `frames/STB03/02-one-worker-faults.svg` {#stb03-fault}
  - Next: stb03-reconnected
- `frames/STB03/03-desktop-closed.svg` {#stb03-closed}
  - Next: stb03-reconnected
- `frames/STB03/04-reconnected.svg` {#stb03-reconnected}

<!-- Traceability: FR-UAE-003#ac-behavior-lifecycle-2, FR-UAE-003#ac-behavior-lifecycle-3, FR-UAE-009#ac-behavior-control-1, TEST-UAE-003, TEST-UAE-009. -->

### Sequence: STB04 Use display, audio, and guest input {#stb04-display-audio-input}

- `frames/STB04/01-display-active.svg`
- `frames/STB04/02-input-directed.svg`
- `frames/STB04/03-media-degraded.svg`

<!-- Traceability: FR-UAE-004#ac-behavior-avinput-1, FR-UAE-004#ac-behavior-avinput-2, FR-UAE-004#ac-behavior-avinput-3, TEST-UAE-004. -->

### Storyboard: STB05 Send a serial command and await a response {#stb05-serial-expectation}

- `frames/STB05/01-early-boot-serial.svg` {#stb05-boot}
  - Next: stb05-pending
- `frames/STB05/02-command-sent.svg` {#stb05-pending}
  - Branch: Matching guest response arrives -> stb05-matched
  - Branch: Bounded expectation expires -> stb05-timeout
- `frames/STB05/03-command-matched.svg` {#stb05-matched}
- `frames/STB05/04-command-timeout.svg` {#stb05-timeout}
  - Loop: Send the command and wait again -> stb05-pending

<!-- Traceability: FR-UAE-010#ac-behavior-serial-1, FR-UAE-010#ac-behavior-serial-2, FR-UAE-010#ac-behavior-serial-3, TEST-UAE-010. -->

### Storyboard: STB06 Debug raw 68040 state {#stb06-debug-machine}

- `frames/STB06/01-machine-running.svg` {#stb06-running}
  - Next: stb06-paused
- `frames/STB06/02-machine-paused.svg` {#stb06-paused}
  - Branch: Step one instruction -> stb06-step
  - Branch: Continue to an execution breakpoint -> stb06-breakpoint
  - Branch: Inspect a mapped guest address -> stb06-mapped
- `frames/STB06/03-step-complete.svg` {#stb06-step}
  - Loop: Step another instruction -> stb06-step
  - Loop: Return to the paused debugger -> stb06-paused
- `frames/STB06/04-breakpoint-hit.svg` {#stb06-breakpoint}
  - Loop: Resume and run to another breakpoint -> stb06-running
- `frames/STB06/05-memory-mapped.svg` {#stb06-mapped}
  - Branch: Query an unmapped address -> stb06-unmapped
  - Loop: Return to the paused debugger -> stb06-paused
- `frames/STB06/06-memory-unmapped.svg` {#stb06-unmapped}
  - Loop: Try another guest address -> stb06-mapped

<!-- Traceability: FR-UAE-011#ac-behavior-machine-1, FR-UAE-011#ac-behavior-machine-2, FR-UAE-011#ac-behavior-machine-3, TEST-UAE-011. -->

### Storyboard: STB07 Mount and safely eject media {#stb07-mount-and-eject}

- `frames/STB07/01-media-browser.svg` {#stb07-browser}
  - Next: stb07-mounted
- `frames/STB07/02-media-mounted.svg` {#stb07-mounted}
  - Next: stb07-confirm
- `frames/STB07/03-eject-confirmation.svg` {#stb07-confirm}
  - Branch: Confirm destructive eject -> stb07-ejected
  - Loop: Cancel and retain the attachment -> stb07-mounted
- `frames/STB07/04-media-ejected.svg` {#stb07-ejected}

<!-- Traceability: FR-UAE-005#ac-behavior-media-1, FR-UAE-005#ac-behavior-media-2, FR-UAE-005#ac-behavior-media-3, TEST-UAE-005. -->
<!-- Decision needed: Define whether a dirty writable attachment offers commit, discard, or both before eject. The current architecture requires confirmation but does not define persistence choices. -->

### Storyboard: STB08 Connect an MCP debugging agent {#stb08-mcp-agent}

- `frames/STB08/01-agent-connect.svg` {#stb08-connect}
  - Next: stb08-ready
- `frames/STB08/02-agent-ready.svg` {#stb08-ready}
  - Branch: Invoke an authorized debugger tool -> stb08-call
  - Branch: Attempt an Operator-only media action -> stb08-denied
  - Branch: Lose the agent transport -> stb08-disconnected
- `frames/STB08/03-agent-call.svg` {#stb08-call}
  - Loop: Invoke another authorized tool -> stb08-call
- `frames/STB08/04-agent-denied.svg` {#stb08-denied}
  - Loop: Choose a permitted debugger tool -> stb08-ready
- `frames/STB08/05-agent-disconnected.svg` {#stb08-disconnected}
  - Loop: Reconnect the agent session -> stb08-connect

<!-- Traceability: FR-UAE-006#ac-behavior-cli-2, FR-UAE-006#ac-behavior-cli-3, FR-UAE-007#ac-behavior-mcp-1, FR-UAE-007#ac-behavior-mcp-2, FR-UAE-007#ac-behavior-mcp-3, TEST-UAE-006, TEST-UAE-007. -->
<!-- Decision needed: Choose whether desktop-managed agent sessions reconnect automatically or require explicit operator action after transport loss. -->

### Storyboard: STB09 Capture, compare, and restore a snapshot {#stb09-snapshot-lifecycle}

- `frames/STB09/01-snapshot-ready.svg` {#stb09-ready}
  - Next: stb09-progress
- `frames/STB09/02-snapshot-progress.svg` {#stb09-progress}
  - Branch: Capture and atomic publication complete -> stb09-created
  - Branch: Worker disconnects during capture -> stb09-failed
- `frames/STB09/03-snapshot-created.svg` {#stb09-created}
  - Branch: Compare with another checkpoint -> stb09-compare
  - Branch: Restore this checkpoint -> stb09-confirm
- `frames/STB09/04-snapshot-compare.svg` {#stb09-compare}
- `frames/STB09/05-restore-confirmation.svg` {#stb09-confirm}
  - Branch: Confirm compatible restore -> stb09-restored
  - Loop: Cancel and keep current state -> stb09-created
- `frames/STB09/06-snapshot-failed.svg` {#stb09-failed}
  - Loop: Retry capture without publishing partial data -> stb09-progress
- `frames/STB09/07-snapshot-restored.svg` {#stb09-restored}

<!-- Traceability: FR-UAE-014#ac-behavior-snapshot-1, FR-UAE-014#ac-behavior-snapshot-2, FR-UAE-014#ac-behavior-snapshot-3, TEST-UAE-014. -->
<!-- Decision needed: Approve snapshot storage format and retention controls before adding archive, delete, and retention-management destinations. -->

### Storyboard: STB10 Run, package, and replay a deterministic test {#stb10-test-and-replay}

- `frames/STB10/01-test-configured.svg` {#stb10-configured}
  - Next: stb10-running
- `frames/STB10/02-test-running.svg` {#stb10-running}
  - Branch: All assertions pass -> stb10-passed
  - Branch: An assertion fails -> stb10-failed
- `frames/STB10/03-test-passed.svg` {#stb10-passed}
- `frames/STB10/04-test-failed.svg` {#stb10-failed}
  - Next: stb10-bundle
- `frames/STB10/05-diagnostic-bundle.svg` {#stb10-bundle}
  - Next: stb10-replay
- `frames/STB10/06-local-replay.svg` {#stb10-replay}
  - Loop: Rerun the captured scenario -> stb10-running

<!-- Traceability: FR-UAE-015#ac-behavior-replay-1, FR-UAE-015#ac-behavior-replay-2, FR-UAE-015#ac-behavior-replay-3, FR-UAE-016#ac-behavior-diagnostics-1, FR-UAE-016#ac-behavior-diagnostics-2, FR-UAE-016#ac-behavior-diagnostics-3, TEST-UAE-015, TEST-UAE-016. -->

### Storyboard: STB11 Resolve symbols or fall back safely {#stb11-symbols}

- `frames/STB11/01-symbols-loaded.svg` {#stb11-loaded}
  - Branch: Symbolicate a controlled crash -> stb11-crash
  - Branch: Load symbols with a mismatched build ID -> stb11-mismatch
- `frames/STB11/02-crash-symbolicated.svg` {#stb11-crash}
- `frames/STB11/03-symbols-mismatch.svg` {#stb11-mismatch}
  - Loop: Load symbols matching the guest build -> stb11-loaded

<!-- Traceability: FR-UAE-012#ac-behavior-symbols-1, FR-UAE-012#ac-behavior-symbols-2, FR-UAE-012#ac-behavior-symbols-3, TEST-UAE-012. -->

### Storyboard: STB12 Negotiate guest-aware runtime inspection {#stb12-guest-runtime}

- `frames/STB12/00-guest-protocol-negotiation.svg` {#stb12-probe}
  - Branch: Compatible guest protocol responds -> stb12-available
  - Branch: No guest protocol is present -> stb12-absent
  - Branch: Guest returns a malformed record -> stb12-malformed
- `frames/STB12/01-guest-protocol-available.svg` {#stb12-available}
  - Next: stb12-container
- `frames/STB12/02-container-detail.svg` {#stb12-container}
- `frames/STB12/03-guest-protocol-absent.svg` {#stb12-absent}
  - Loop: Probe again while raw debugging remains active -> stb12-probe
- `frames/STB12/04-guest-protocol-malformed.svg` {#stb12-malformed}
  - Loop: Retry after the guest service is corrected -> stb12-probe

<!-- Traceability: FR-UAE-013#ac-behavior-guest-1, FR-UAE-013#ac-behavior-guest-2, FR-UAE-013#ac-behavior-guest-3, TEST-UAE-013. -->
<!-- Decision needed: Define the negotiated guest command surface, if any. Current requirements support inspection and explicitly leave guest-side command execution conditional. -->

### Storyboard: STB13 Enforce Observer capabilities {#stb13-observer-capabilities}

- `frames/STB13/01-observer-session.svg` {#stb13-observer}
  - Branch: Read the selected instance state -> stb13-read
  - Branch: Attempt to write guest memory -> stb13-denied
- `frames/STB13/02-observer-read.svg` {#stb13-read}
- `frames/STB13/03-observer-write-denied.svg` {#stb13-denied}

<!-- Traceability: FR-UAE-008#ac-behavior-security-1, FR-UAE-008#ac-behavior-security-2, FR-UAE-009#ac-behavior-control-1, FR-UAE-009#ac-behavior-control-2, FR-UAE-009#ac-behavior-control-3, TEST-UAE-008, TEST-UAE-009. -->

### Sequence: STB13 Reject an invalid HTTP Origin {#stb13-invalid-origin}

- `frames/STB13/04-invalid-origin.svg`

<!-- Traceability: FR-UAE-008#ac-behavior-security-3, TEST-UAE-008. -->

### Storyboard: STB14 Edit, build, boot, and debug a UAE project {#stb14-build-run-debug}

- `frames/STB14/01-edit-with-breakpoint.svg` {#stb14-edit}
  - Branch: Start Debugging and build the active launch configuration -> stb14-building
- `frames/STB14/02-building.svg` {#stb14-building}
  - Branch: Build fails and records a source location -> stb14-problem
  - Branch: Build succeeds and the selected target starts -> stb14-launch
- `frames/STB14/03-build-error.svg` {#stb14-problem}
  - Branch: Open the problem in the editor and fix the typo -> stb14-fixed
- `frames/STB14/04-fix-source.svg` {#stb14-fixed}
  - Loop: Save and start debugging again with the same target -> stb14-building
- `frames/STB14/05-debug-launch.svg` {#stb14-launch}
  - Next: stb14-emulation
- `frames/STB14/06-emulation-running.svg` {#stb14-emulation}
  - Branch: Guest reaches the source breakpoint -> stb14-break
- `frames/STB14/07-breakpoint-hit.svg` {#stb14-break}
  - Branch: Step into the next instruction -> stb14-step
  - Loop: Continue and return to the Emulation tab -> stb14-emulation
- `frames/STB14/08-step-into.svg` {#stb14-step}
  - Loop: Continue and return to the Emulation tab -> stb14-emulation

<!-- Traceability: FR-UAE-001#ac-behavior-desktop-3, FR-UAE-002#ac-behavior-profile-1, FR-UAE-011#ac-behavior-machine-1, FR-UAE-012#ac-behavior-symbols-1, TEST-UAE-001, TEST-UAE-002, TEST-UAE-011, TEST-UAE-012. -->
<!-- Decision needed: Choose the user-configurable build task and how it resolves symbols, ROM and boot disk for a launch configuration. The Rust project and compiler error shown here are illustrative. Build Project alone reports diagnostics. Start Debugging runs the build and launches only on success. -->
<!-- Command traceability: FR-RETRO-007, TR-RETRO-COMMAND-007, TEST-RETRO-007. Menu, bar, and keyboard bindings share each registered MVVM Toolkit command instance. Build Project and Start Debugging use AsyncRelayCommand and raise CommandRequested on the registered IView. The main SimpleMVC controller dispatches the request to the custom CQRS Unit of Work and delivers the result to the originating view on the Avalonia UI thread. -->
<!-- Tabbed MDI: An editor tab registers directly with its owning DocumentController. Document actions use the active editor's binding and capture that editor as the result recipient. See the DocumentMdiSave C4 view. -->
<!-- Source editor traceability: FR-RETRO-011, TR-RETRO-EDITOR-011, TEST-RETRO-011. STB14 illustrates an AvaloniaEdit source tab with a breakpoint gutter and navigation from build diagnostics. See the SourceEditorWorkflow C4 view. -->

### Sequence: STB15 Seek and adjust volume in the Emulation player {#stb15-emulation-player}

- `frames/STB15/01-replay-open.svg`
- `frames/STB15/02-replay-seeking.svg`
- `frames/STB15/03-replay-volume.svg`

<!-- Interaction: In a recorded replay, drag the handle under the time readout. The playhead and guest preview follow the selected time. Release to leave replay paused at that time. Adjust Volume independently of the playhead. For a live session, the player shows elapsed time and leaves seek unavailable. -->
<!-- Design proposal: Seeking assumes an indexed recording or seekable replay source. Rendering these frames does not establish emulator support for timeline seeking. -->
<!-- Retro IDE traceability: FR-RETRO-003#ac-seek-volume, TEST-RETRO-003. -->

### Storyboard: STB16 Select an emulator target {#stb16-select-target}

- `frames/STB16/01-select-target.svg` {#targets}
  - Branch: Select UAE and open its profile -> uae-profile
  - Branch: Select VICE and launch x64sc -> vice-running
  - Branch: Select ViceSharp and attach to a host -> vice-sharp-running
- `frames/STB02/01-profile-draft.svg` {#uae-profile}
  - Loop: Change target -> targets
- `frames/STB16/02-vice-emulation.svg` {#vice-running}
  - Loop: Change target -> targets
- `frames/STB16/03-vice-sharp-emulation.svg` {#vice-sharp-running}
  - Loop: Change target -> targets

<!-- Retro IDE traceability: FR-RETRO-002#ac-target-presets, TEST-RETRO-002. The adapter connections shown are design scenarios. -->
