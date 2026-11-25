# Retro IDE

Retro IDE is a shared Windows and Linux development environment for retrocomputing emulators. The initial Avalonia workbench has docked and floating tools, document tabs including Emulation, and a focus command shared by the menu, command bar, shortcut, and MCP clients. Project browsing, editing, Git, and emulator integration are planned in the [implementation plan](docs/plans/PLAN-RETROIDE-001.md).

The Phase 01 candidate remains under independent review. See the [verified Windows and Linux results](docs/receipts/phase-01-remotecontrol-verification.md).

The initial target presets are [UAE](profiles/uae.json), [VICE](profiles/vice.json), and [ViceSharp](profiles/vice-sharp.json). Each selects an emulator bundle from the planned **Emulators** manager. The [bundle publishing and adapter design](docs/architecture.md#emulator-bundles-and-buckets) describes the catalog, package lifecycle, and integration for each target.

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

Run `dotnet test RetroIde.sln -c Release --no-build` for the managed tests. The UI suite drives the workbench through SharpNinja.Avalonia.RemoteControl on Avalonia Headless and needs no physical keyboard or mouse. RemoteControl is loaded only by the test projects. Run `pwsh -File eng/Verify-Phase01Trace.ps1 -QBrainExport docs/requirements/phase-01-test-criteria.yaml` to verify the compiled requirement trace.

The Windows build also runs `dotnet run --project tests/RetroIde.NativeTests -c Release --no-build -- --remote-control` to focus a real Win32 workbench and verify distinct main and floating windows through Dock’s float and redock operations.

The separate native smoke test checks platform cursor rendering and the interactive tear-away and redock gestures. Run `dotnet run --project tests/RetroIde.NativeTests -c Release --no-build` from an available Windows desktop. The Windows CI job runs this smoke test when `RETRO_IDE_RUN_NATIVE_POINTER_TESTS` is set to `1`. Linux CI runs it on an isolated Xvfb display with the static X11 core cursor.

## Review the design

Regenerate the screen SVGs and review them locally from the repository root with PowerShell and AssetReview installed.

```powershell
pwsh -NoProfile -File ./docs/design/build_wireframes.ps1
asset-review --asset-root docs/design --title "Retro IDE"
```
