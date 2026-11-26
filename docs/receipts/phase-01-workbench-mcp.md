# Retro IDE Phase 01 verification

The exact source archive was `269f4219f6ccf46fd8777d4a5ecb0e86b9103fda8986119fb0e2c1458557b5d2`. Phase 01 uses the [reconciled verification policy](../plans/PLAN-RETROIDE-001.md#local-verification-for-every-phase). Windows verification ran in session 0 on LEGION2. Linux used isolated Xvfb displays `:234` and `:235` for the platform suite and `:236` for RemoteControl.

- All 27 managed tests passed on Linux and Windows. The Release builds completed with analyzer warnings and zero errors.
- SharpNinja.Avalonia.RemoteControl focused the real toolbar, verified selection, floated one native tool window, and checked redock into the original group. It verified the scoped Wait cursor and its rendered hit target in docked and floating windows, and ran an available toolbar command while the affected pane remained blocked. It also verified overlap, failure, cancellation, and cleared input overrides.
- The isolated Linux platform suite sampled the effective X11 cursor and exercised drag-out and redock gestures. The Win32 suite observed native window identities, rendered hit testing, and the cursor assigned through Avalonia.
- Two separate workbench processes demonstrated attachment isolation and the same focus outcome through the UI, stdio, and Streamable HTTP using the public ViewModel contract. A fresh process restored the captured layout. The separate adapter fixture retained state across workbench close and reopen, with no desktop assemblies in its dependencies or loaded set.
- The compiled gate verified 42 test links and 32 documented public contracts across 14 Phase 01 QBrain criteria. Structurizr validated the accepted model and exported 19 diagrams.

## QBrain criterion acceptance

The Phase 01 writeback records 8 functional, 16 technical, and 10 test criteria as satisfied with evidence from the accepted HV slices. FR-RETRO-001 records docking as accepted. Its layout criterion still needs verification against a running production emulator in Phase 05. The complete instance-scoped pane criterion remains open for Phase 08.

## Independent results

| Slice | QBrain item | Verdict | Accuracy | Completeness |
| --- | --- | --- | --- | --- |
| 01A | EXEC-TODO-001 | AGREE | 100 | 100 |
| 01B | EXEC-TODO-002 | AGREE | 100 | 100 |
| 01C | EXEC-TODO-003 | AGREE | 99 | 99 |
| 01D | EXEC-TODO-004 | AGREE | 100 | 100 |

QBrain preserves the per-task acceptance evidence and the paired records of the independent review. The task IDs above identify those records.

## Reconciled independent review

OverallVerdict: AGREE

OverallAccuracy: 100

OverallCompleteness: 100

ValidatorIdentity: ChatGPT-6.2-SOL-HIGH-LEGION2

Task 01A EXEC-TODO-001: AGREE (accuracy 100, completeness 100)

Task 01D EXEC-TODO-004: AGREE (accuracy 100, completeness 100)

### 01A · Docking and adapter isolation

- **PASS.** RemoteControl invoked the toolbar on the real Win32 workbench and verified that Registers became the selected, focused Dock tab with one revision increment. Dock’s factory floated Registers to a distinct HWND and returned it to its original group and root.
- **PASS.** The Linux display-isolation gap is resolved. The launcher checked that the X11 socket and lock were absent, started Xvfb with `-nolisten tcp`, and logged the inherited `DISPLAY`. The XTEST drag-out and pointer redock passed on `:234` and `:235`. The RemoteControl lifecycle passed on `:236`.
- **PASS.** The independent adapter fixture ran in a separate PID. Its dependency manifest and loaded assembly list showed no desktop dependencies. The deliberate Avalonia dependency failed the manifest assertion.

### 01D · Layout, scoped Wait, and command presentation

- **PASS.** `ShellLifecycleTests` cover restored tool and document selection, original pin ownership, recovery of panes from closed floating hosts, and active split-document restoration with duplicate group IDs. The fixture retained its running session identity, advanced while the workbench was closed, and showed no layout-driven state change. The production restart separately restored Registers and published a fresh ViewModel registration.
- **PASS.** On docked and floating HWNDs and XIDs, the suite verified the Avalonia `StandardCursorType.Wait` assignment, center hit testing on the overlay, disabled pane content, and a nonempty application frame while the block was pending. It exercised background work and overlapping success, cancellation, and failure, then verified cursor and input restoration. The available-command gap is resolved on both platforms. The main toolbar started a fresh task for Output during the docked block (`1 → 2`) and a fresh task for Explorer during the floating block (`4 → 5`). Linux separately sampled the effective X11 cursor and its restoration through XFixes. The signed-in Windows run is supplemental.
- **PASS.** The bundled English catalog and `WorkbenchWindow` show that the menu and toolbar derive their label, icon, and shortcut presentation from one descriptor. The passing `Menu_toolbar_and_shortcut_share_one_eligible_focus_command` test verifies shared command identity, eligibility, one dispatch per eligible route, suppression of the disabled shortcut, and rejection of a duplicate gesture in the same scope. The Linux native run also exercised toolbar, menu, and keyboard invocation.

### Evidence and boundaries

The independent LEGION2 harness reported successful snapshot checksum checks before and after the run, a Release build with 68 warnings and zero errors, and 2 unit plus 25 integration tests with zero failures or skips. PowerShell execution policy blocked the Windows trace script before it ran. Under MEMORY-HV-100 rule 6, the exact-source Linux trace receipts cover both QBrain inputs with 42 test links, 32 documented public contracts, and 14 distinct criteria. I reviewed the supplied source and receipts without running commands or accessing external sources. No scoped FAIL or UNKNOWN remains.

**01B and 01C remain QBrain-accepted and regression-only.** Their shared workbench scope had managed dispatch, result-routing, and context coverage, plus live toolbar, HTTP, and stdio focus on the selected desktop and attachment invalidation on close. **Phases 02 through 08 remain planned and outside this acceptance**, including production adapter reattach, complete instance-scoped views, full localization, and the full MCP inventory.
