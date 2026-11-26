# Retro IDE

Retro IDE is a shared Windows and Linux development environment for retrocomputing emulators. The initial Avalonia workbench has docked and floating tools, document tabs including Emulation, and a focus command shared by the menu, command bar, shortcut, and MCP clients. The Phase 02 candidate adds Project Explorer, AvaloniaEdit source tabs, and language packs. The fresh candidate passed 78 managed tests per host and the native UI suites. Phase 02 remains open on incomplete test-first evidence. The [implementation plan](docs/plans/PLAN-RETROIDE-001.md) tracks each phase.

Phase 01 passed the shared workbench focus and input-free native UI gates on Windows and Linux. [Verification and independent acceptance](docs/receipts/phase-01-workbench-mcp.md)

The initial target presets are [UAE](profiles/uae.json), [VICE](profiles/vice.json), and [ViceSharp](profiles/vice-sharp.json). Each selects an emulator bundle from the planned **Emulators** manager. The [bundle publishing and adapter design](docs/architecture.md#emulator-bundles-and-buckets) describes the catalog, package lifecycle, and integration for each target.

The debugger design describes how an emulator package supplies the machine configuration and platform-specific debugger layouts. The [package debugger storyboard](docs/design/_manifest.md#stb17-package-debugger) follows the workflow through CPU selection, clock phases, banking, and the memory display byte-order preference.

- [Wireframe and storyboard manifest](docs/design/_manifest.md)
- [Shared SVG components](docs/design/components/README.md)
- [Architecture overview](docs/architecture.md)
- [Git integration](docs/git-integration.md)
- [Language server support](docs/language-servers.md)
- [MCP tools](docs/mcp-tools.md)
- [QBrain requirement ownership](docs/requirement-ownership.md)
- [Emulator adapter contract](docs/emulator-adapter-contract.md)
- [Structurizr C4 model](docs/architecture/structurizr/README.md)

## Run the workbench

```sh
dotnet build RetroIde.sln -c Release
dotnet run --project src/RetroIde.Workbench -c Release --no-build
```

Start the desktop with `-- --mcp` to issue an observer attachment or `-- --mcp-mutate` to allow the attached client to focus tools. The workbench shows the path to its private attachment file. A stdio client can launch `dotnet` with the built `RetroIde.Mcp.Stdio.dll` and that file path as arguments. A Streamable HTTP client uses the loopback endpoint and bearer token from the same file. Call `workbench_get_context` to capture the public ViewModel context, then supply that context with a tool ID such as `registers` to `workbench_focus_tool`.

Run `dotnet test RetroIde.sln -c Release --no-build` for the managed tests. The UI suite drives the workbench through SharpNinja.Avalonia.RemoteControl on Avalonia Headless and needs no physical keyboard or mouse. RemoteControl is loaded only by the test projects. Run `pwsh -File eng/Verify-Phase01Trace.ps1` for the Phase 01 compiled trace. For the Phase 02 candidate, add `-ThroughPhase 02` and pass `-QBrainExport` with the effective requirements export to check the criterion IDs against QBrain.

For the native-window UI suite on Windows and Linux, run `dotnet run --project tests/RetroIde.NativeTests -c Release --no-build -- --remote-control`. With SharpNinja.Avalonia.RemoteControl, the suite exercises the scoped Wait cursor and native floating and redocking. The Phase 02 candidate adds mixed-root Explorer, source editing and save, and live localization and persistence. It uses application-scoped input and fixture paths for file selection.

The Linux platform smoke also checks effective X11 cursor rendering and interactive tear-away and redock gestures. For Linux, choose an explicit unused display number and verify that its socket and lock are absent before launching the test. The example uses display `:177`.

```sh
test ! -e /tmp/.X11-unix/X177 && test ! -e /tmp/.X177-lock &&
  XCURSOR_THEME=core XCURSOR_PATH=/nonexistent xvfb-run -n 177 \
    -s '-screen 0 1600x1000x24 -extension GLX' \
    dotnet run --project tests/RetroIde.NativeTests -c Release --no-build
```

Run acceptance on the designated local development hosts. Build `win-x64` on LAB-OMARCHY, transfer a verified runtime bundle, and execute the managed and RemoteControl suites on LEGION2. The signed-in Windows pointer smoke is supplemental. [Local verification policy](docs/plans/PLAN-RETROIDE-001.md#local-verification-for-every-phase) · [Phase 02 verification status](docs/plans/PLAN-RETROIDE-001.md#phase-02-verification-status)

## Review the design

Regenerate the screen SVGs and review them locally from the repository root with PowerShell and AssetReview installed.

```powershell
pwsh -NoProfile -File ./docs/design/build_wireframes.ps1
asset-review --asset-root docs/design --title "Retro IDE"
```
