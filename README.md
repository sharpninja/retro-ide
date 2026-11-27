# Retro IDE

Retro IDE is a shared Windows and Linux development environment for retrocomputing emulators. The Avalonia workbench includes docked and floating tools, document tabs including Emulation, Project Explorer, AvaloniaEdit source tabs, and language packs. The desktop and its scoped MCP clients share command and context contracts. The [implementation plan](docs/plans/PLAN-RETROIDE-001.md) tracks each phase.

Phases 01 and 02 passed independent review and native UI verification on Windows and Linux. Phase 02 passed 130 managed tests per host. [Phase 01 verification](docs/receipts/phase-01-workbench-mcp.md) · [Phase 02 verification](docs/receipts/phase-02-workbench-explorer-editor.md)

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

Start the desktop with `-- --mcp` to issue an observer attachment or `-- --mcp-mutate` to allow scoped writes. The workbench shows the path to its private attachment file. A stdio client can launch `dotnet` with the built `RetroIde.Mcp.Stdio.dll` and that file path as arguments. A Streamable HTTP client uses the loopback endpoint and bearer token from the same file. Call `workbench_get_context` to capture the public ViewModel context, then supply that context with a tool ID such as `registers` to `workbench_focus_tool`. [Explorer, editor and language tools](docs/mcp-tools.md)

Run the Nuke managed gate or select specific test classes from the repository root. Give each run a unique ID. The gates write a TRX for each selected project under `TestResults/<run-id>/`.

```powershell
dotnet run --project eng/Nuke/RetroIde.Build.csproj -c Release -- Test --test-run-id managed-01
dotnet run --project eng/Nuke/RetroIde.Build.csproj -c Release -- TestGroups --test-groups 'RetroIde.IntegrationTests:GitDiffIndexTests' --test-run-id git-01
dotnet run --project eng/Nuke/RetroIde.Build.csproj -c Release -- ValidateTraceability --through-phase 03 --qbrain-export /path/to/effective-requirements.yaml
```

Run `TestNative` with the same Nuke project for the native-window suite. SharpNinja.Avalonia.RemoteControl drives the workbench without physical keyboard or mouse input. The suite checks the scoped Wait cursor, floating and redocking, mixed-root Explorer, editor workflows, localization, and Git tooling. On Linux, run it on an explicitly selected unused test display.

The Linux platform smoke also checks effective X11 cursor rendering and interactive tear-away and redock gestures. For Linux, choose an explicit unused display number and verify that its socket and lock are absent before launching the test. The example uses display `:177`.

```sh
test ! -e /tmp/.X11-unix/X177 && test ! -e /tmp/.X177-lock &&
  XCURSOR_THEME=core XCURSOR_PATH=/nonexistent xvfb-run -n 177 \
    -s '-screen 0 1600x1000x24 -extension GLX' \
    dotnet run --project tests/RetroIde.NativeTests -c Release --no-build
```

Run acceptance on the designated local development hosts. Build `win-x64` on LAB-OMARCHY, transfer a verified runtime bundle, and execute the managed and RemoteControl suites on LEGION2. The signed-in Windows pointer smoke is supplemental. [Local verification policy](docs/plans/PLAN-RETROIDE-001.md#local-verification-for-every-phase) · [Phase 02 verification status](docs/plans/PLAN-RETROIDE-001.md#phase-02-verification-status)

### Install on Omarchy

Run the standalone NUKE target from the repository root on a Linux x64 host with the .NET 10 SDK and ASP.NET Core runtime.

```sh
dotnet run --project eng/Nuke/RetroIde.Build.csproj --configuration Release -- DeployOmarchy
```

The target publishes the workbench and MCP stdio bridge, validates the bundled command resources, and places a release under the user’s XDG data directory. It registers **Retro IDE** in the application launcher and installs `retro-ide` and `retro-ide-mcp` in `~/.local/bin`. Previous releases stay available under `retro-ide/releases`.

The workbench uses X11 when it can connect to the display. On a Wayland desktop with an unavailable X11 display, it opens directly through Wayland.

## Review the design

Regenerate the screen SVGs and review them locally from the repository root with PowerShell and AssetReview installed.

```powershell
pwsh -NoProfile -File ./docs/design/build_wireframes.ps1
asset-review --asset-root docs/design --title "Retro IDE"
```
