# Phase 01 input-free workbench verification

SharpNinja.Avalonia.RemoteControl Runtime 0.8.0 is pinned in the test projects. The tests locate the real toolbar and menu by automation ID and activate them through Avalonia. The native RemoteControl runner hosts the production workbench in separate processes and drives test commands through their inherited input pipes. The tests use the real HTTP and stdio MCP endpoints and the production layout save path.

The frozen source used in both host runs has SHA256 `8ef29a1b4cc3f8b80f96e66295d99f763cf618d3c1cac644b155346650ac2d1d`.

| Check | Result |
| --- | --- |
| Linux and LEGION2 Release | Each host passed 2 unit and 23 integration tests with zero failures or skips. The builds reported analyzer warnings and zero errors. |
| LEGION2 input-free Win32 | RemoteControl focused Registers through the production toolbar. Separate HWNDs verified floating and redocking. Both docked and floated windows rendered frames during an interaction block and maintained their Wait state while overlapping operations succeeded, failed, or canceled. |
| Linux native input | The X11/XTEST suite passed on isolated displays `:211` and `:212`. It verified toolbar, menu, shortcut, docking, and the effective platform pointer. The input-free suite passed on `:213`. |
| Production process isolation | The UI, Streamable HTTP, and stdio focused Registers at revisions 2, 4, and 6. Both MCP clients rejected the other workspace. The other process stayed active when the selected process closed. A fresh process restored the saved tool selection and published a new ViewModel registration. |
| Compiled requirements trace | The gate checked 40 test links, all 32 public product types, and 14 scoped QBrain criteria. Negative controls rejected a missing required criterion, an unlinked public type, an annotated non-test helper, an explicitly skipped case, and an ambiguous test overload. |

Win32 rendering and the scoped cursor property are covered by the input-free run. The effective pointer was observed on Linux in the current run and on Windows in the earlier signed-in smoke test. The current Windows run left the signed-in desktop available.

The current test workflow is documented in the [project README](../../README.md) and the [Phase 01 plan](../plans/PLAN-RETROIDE-001.md).
