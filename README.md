# Retro IDE

Retro IDE is a shared Windows and Linux development environment for retrocomputing emulators. The current design checkpoint contains architecture, versioned C4 diagrams, wireframes, storyboards, and target presets. The planned desktop includes Project Explorer, Git integration, an AvaloniaEdit source editor with language servers, an Emulation player, and a bundle manager. MCP tools use the ViewModels as public context contracts.

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

## Review the design

Regenerate the screen SVGs and review them locally from the repository root with PowerShell and AssetReview installed.

```powershell
pwsh -NoProfile -File ./docs/design/build_wireframes.ps1
asset-review --asset-root docs/design --title "Retro IDE"
```
