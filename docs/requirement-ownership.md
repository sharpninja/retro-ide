# QBrain requirement ownership

The QBrain workspace at `/home/sharpninja/github/retro-ide` owns the desktop and its tests. Each row maps to the correspondingly numbered TEST-RETRO record and its linked Retro IDE technical requirement.

| Retro IDE | Source scope | Ownership retained by UAE Developer |
| --- | --- | --- |
| FR-RETRO-001 | FR-UAE-001, TR-UAE-DESKTOP-001, TEST-UAE-001 | UAE control host lifecycle under TR-UAE-LIFECYCLE-003. The original desktop records are deferred with migration references for traceability. |
| FR-RETRO-002 | Desktop portions of FR-UAE-002, FR-UAE-003, and FR-UAE-009 | UAE40 profile and boot conformance, worker isolation, leases, and revisions |
| FR-RETRO-003 | Desktop portions of FR-UAE-004 and FR-UAE-015 | Frame, audio, input, and deterministic replay contracts at the UAE boundary |
| FR-RETRO-004 | Desktop portions of FR-UAE-010 through FR-UAE-013 | Serial, 68040 and AGA observability, symbols, and the A1200 OS guest protocol |
| FR-RETRO-005 | Desktop portions of FR-UAE-005 and FR-UAE-014 through FR-UAE-016 | UAE storage, snapshots, test execution, and diagnostic artifact behavior |
| FR-RETRO-006 | Desktop portions of FR-UAE-006 through FR-UAE-008 | UAE headless CLI, MCP endpoint, authorization, and worker lifetime |
| FR-RETRO-007 | Retro IDE menus, command bars, shared MVVM Toolkit commands, IView controller ownership, CQRS, Avalonia result delivery, and system wait cursor for logically blocked UI | Backend authorization and control operations remain with the selected adapter host |
| FR-RETRO-008 | Retro IDE command icons, label slugs, default keyboard shortcuts, and typed localization of strings and images | IDE localization applies to IDE-owned presentation. Backends supply operation and diagnostic data. |
| FR-RETRO-009 | Emulator bundle discovery, publishing manifests, installation, versions, and adapter resolution through the Emulators manager | UAE Developer owns the UAE worker and control behavior included in the UAE bundle. |
| FR-RETRO-010 | Versioned emulator adapter contract, session lifecycle, capability ports, command results, events, and streams | UAE Developer owns the backend protocol, worker, and host-side capability and authorization behavior. |
| FR-RETRO-011 | AvaloniaEdit source editor, document buffer and save behavior, and source and breakpoint navigation | The selected adapter supplies debugger capabilities and results. Retro IDE owns source presentation and navigation. |

Retained UAE requirements describe what the headless control or adapter boundary must expose. Retro IDE requirements cover presentation and interaction across adapters. Implementation evidence stays with the repository and requirement it verifies.
