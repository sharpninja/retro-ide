# Shared wireframe SVGs

Edit these sources for changes shared across storyboard frames.

| File | Artwork |
| --- | --- |
| [workbench.svg](workbench.svg) | Typography, title bar, menus, toolbar, and workbench layout |
| [docking.svg](docking.svg) | Tool-window headers, tab strips, and docking guide |
| [emulation.svg](emulation.svg) | Guest viewport, player chrome, and slider handles |
| [icons.svg](icons.svg) | Toolbar, tab, docking, and player icons |
| [dialog.svg](dialog.svg) | Confirmation dialog and buttons |
| [debugger.svg](debugger.svg) | Debugger selectors, byte-order controls, and table rows |

Use a unique ID for each element in a source file's `<defs>`. Components can reference one another with `<use href="#id">`. The generator copies only the definitions each frame uses, including nested references. Text and geometry that vary by state, such as time and volume, are supplied by the generator.

Regenerate the standalone frame SVGs from the repository root.

```powershell
pwsh -NoProfile -File ./docs/design/build_wireframes.ps1
```
