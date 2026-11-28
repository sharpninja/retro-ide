workspace "Retro IDE" "Shared emulator IDE with a dockable workbench, source tools, native Git and terminals, language services, pinned emulator packages, and scoped MCP tools." {
    !identifiers hierarchical

    model {
        developer = person "Retro Developer" "Edits, builds, runs, tests, and debugs software for a selected retrocomputing target."

        retro = softwareSystem "Retro IDE" "Shared cross-platform development environment and capability-driven emulator integrations." {
            properties {
                "Baseline" "implementation-04-emulator-manager-adapter-conformance"
                "Implementation status" "Phases 01 to 03 deliver the workbench, source tools, localization, native Git and terminals, language services, and scoped MCP. Phase 04 delivers pinned emulator package management and shared session and machine contracts verified with fixture providers. Shipped provider integration is Phase 05 and debugger panes are Phase 06."
                "QBrain workspace" "/home/sharpninja/github/retro-ide"
            }

            desktop = container "Retro IDE Desktop" "Owns the workbench, source tools, package management, and shared emulator session contracts." ".NET, Avalonia, Dock.Avalonia" "Phase 01,Phase 02,Phase 03,Phase 04" {
                shell = component "Workbench and Docking (View)" "Hosts menus, command bars, keyboard bindings, documents, and docked tools. Presents command metadata and localization, and applies the system wait cursor to logically blocked UI." "Avalonia and Dock.Avalonia" "Phase 01" {
                    properties {
                        "Requirements" "FR-RETRO-001, TR-RETRO-DESKTOP-001, FR-RETRO-007, FR-RETRO-008, TR-RETRO-COMMAND-007#ac-system-wait-cursor"
                        "Storyboard" "STB01 Arrange tool windows and restore an IDE workspace"
                    }
                }
                commands = component "Predefined Command Catalog" "Registers typed IDE commands and queries with stable IDs and required capabilities. Pairs UI commands with Toolkit bindings and localized metadata. Supplies the MCP tool registry." "CommunityToolkit.Mvvm and typed .NET requests" "Phase 01" {
                    properties {
                        "Requirements" "FR-RETRO-007, TR-RETRO-COMMAND-007, FR-RETRO-008, TR-RETRO-LOCALIZATION-008"
                    }
                }
                localizer = component "Localization Service" "Resolves typed text and image slugs through exact locale, parent locales, and bundled English. Formats messages and refreshes workbench presentation when the UI culture changes." ".NET localization" "Phase 01,Phase 02" {
                    properties {
                        "Requirements" "FR-RETRO-008, TR-RETRO-LOCALIZATION-008"
                    }
                }
                localeCatalog = component "Locale Catalogs" "Supplies versioned catalogs of strings, message patterns, and local SVG or PNG resources. Includes the bundled default catalog and validated language packs." "BCP 47 locale tags" "DataStore,Phase 01,Phase 02"
                controller = component "Main MVC Controller" "Owns dispatch and result delivery for views registered directly with it. Provides the shared execution entry point for typed requests from all controllers into the custom Unit of Work." "GPS.SimpleMVC 2.0.0" "Phase 01"
                documentController = component "Document Controller" "Owns dispatch for editor views that register directly with it. Receives their events, dispatches typed document requests through the shared CQRS entry point, and delivers results to the originating tab." "GPS.SimpleMVC 2.0.0" "Phase 02" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-controller-ownership, TR-RETRO-COMMAND-007#ac-mdi-dispatch"
                        "Storyboard" "STB14 Edit, build, boot, and debug a UAE project"
                    }
                }
                unitOfWork = component "CQRS Unit of Work" "Owns IDE application logic. Dispatches typed commands and read-only queries to registered handlers with scoped context, validates eligibility, and returns or publishes their outcomes." "Custom .NET command and query handlers" "Phase 01" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-cqrs-unit-of-work"
                    }
                }
                viewModels = component "IDE ViewModels (View)" "Implement IView with a stable ViewKey. Raise CommandRequested, receive correlated results on the UI thread, and expose observable properties through ObservableObject." "CommunityToolkit.Mvvm.ComponentModel and SimpleMVC" "Phase 01" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-simplemvc-views, TR-RETRO-COMMAND-007#ac-toolkit-viewmodels"
                    }
                }
                documentViews = component "Editor Tab ViewModels (View)" "One observable IView per editor tab. The ViewModel defines the public MCP context for its workspace, document, view key, and revision." "CommunityToolkit.Mvvm.ComponentModel and SimpleMVC" "Phase 02"
                languageServices = component "Language Services" "Manages configured server processes, workspace roots and language bindings. Synchronizes versioned documents, queues committed saves before subsequent close, and correlates negotiated completion, navigation, diagnostics, and edit requests." ".NET, framed LSP JSON-RPC over stdio" "Phase 03" {
                    properties {
                        "Requirements" "FR-RETRO-014, TR-RETRO-LSP-014, TEST-RETRO-014"
                        "Source" "LanguageServerWorkspace, LanguageServerSession, LanguageServerFeatures"
                    }
                }
                workspaceExplorer = component "Project Explorer" "Dockable tree of project and folder roots. Restores stable roots, expansion and selection. Loads children on demand, refreshes affected expanded branches, and offers Open and Reveal for the active workspace." "Avalonia and Dock.Avalonia" "Phase 02" {
                    properties {
                        "Requirements" "FR-RETRO-012, TR-RETRO-EXPLORER-012"
                        "Storyboards" "STB01 Workspace layout and STB14 Source editing"
                    }
                }
                workspaceWatcher = component "Explorer Filesystem Watcher" "Coalesces external changes by workspace root and directory, tracks missing roots, and requests refresh for affected expanded branches on the Avalonia dispatcher." ".NET FileSystemWatcher" "Phase 02" {
                    properties {
                        "Requirements" "FR-RETRO-012, TR-RETRO-EXPLORER-012, TEST-RETRO-012"
                    }
                }
                sourceControl = component "Source Control" "Dockable repository and worktree view for status, diffs, staging, history, branches, remotes, and conflict workflows. Binds the registered Git commands." "Avalonia and Dock.Avalonia" "Phase 03" {
                    properties {
                        "Requirements" "FR-RETRO-013, TR-RETRO-GIT-013"
                    }
                }
                gitIntegration = component "Git Service" "Runs installed Git for a captured checkout. Reads machine state, validates reviewed Git mutations, publishes prepared index updates, and reports typed results for repository, conflict, worktree, submodule, and LFS workflows." ".NET handlers and native Git CLI" "Phase 03" {
                    properties {
                        "Requirements" "FR-RETRO-013, TR-RETRO-GIT-013, TEST-RETRO-013"
                        "Source" "GitFeatures, NativeGit, PreparedGitIndex"
                    }
                }
                gitMutationLease = component "Git Checkout Validation" "Acquires the shared write gate, then discovers the worktree again and checks the resolved worktree, Git directory, and common directory paths against the captured selection. Releases the gate on failure." ".NET GitMutationLease" "Phase 03" {
                    properties {
                        "Requirements" "TR-RETRO-GIT-013#ac-git-machine-state, TR-RETRO-GIT-013#ac-git-state-consistency"
                    }
                }
                mutationGate = component "Workspace Write Gate" "Coordinates reviewed Git writes, repository setup, prepared index publication, and editor file replacement across worktrees in the same IDE process." ".NET asynchronous semaphore" "Phase 03"
                documentPublication = component "Editor File Publication" "Stages the captured source text, waits for the shared write gate, and replaces the file after the originating registration and save revision are rechecked." ".NET PreparedDocumentSave" "Phase 02,Phase 03"
                terminal = component "Integrated Terminal" "Validates the selected checkout before starting the native shell. Supports scoped tab and MCP input, resizing, bounded transcript reads, and exit state." ".NET and Porta.Pty" "Phase 03" {
                    properties {
                        "Requirements" "FR-RETRO-013, TR-RETRO-GIT-013#ac-git-terminal, TEST-RETRO-013"
                    }
                }
                sourceEditor = component "Source Editor" "Keeps independent tab buffers, selection, caret, undo and redo through floating and redocking. Provides find and replace, syntax and plain fallback, and diagnostic fixture navigation. Live build diagnostics and debugger markers are later integrations." "AvaloniaEdit.TextEditor" "Phase 02" {
                    properties {
                        "Requirements" "FR-RETRO-011, TR-RETRO-EDITOR-011"
                        "Storyboard" "STB14 Edit, build, boot, and debug a UAE project"
                    }
                }
                uiDispatcher = component "Avalonia UI Dispatcher" "Schedules callbacks on the UI thread. The dispatch owner checks its originating registration inside the callback before applying the result." "Avalonia Dispatcher.UIThread" "Implemented"
                ideState = component "IDE State (Model)" "Provides observable workspace, presentation culture, active document and target, selected session, availability, operation state, interaction blocks by scope, and diagnostics." ".NET" "Phase 01"
                launch = component "Project and Launch Workflow" "Selects the target preset, runs the configured build, opens source diagnostics, and launches or attaches after a successful build." ".NET" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-002, TR-RETRO-PROFILE-002"
                        "Storyboards" "STB14 Edit, build, boot, and debug a UAE project; STB16 Select an emulator target"
                    }
                }
                sessions = component "Session Coordinator" "Negotiates the installed binding's contract, validates the opened session identity and close policy, and transfers the package lease to the pinned session. Fixture adapters verify launch, attach, reconnect, and release." ".NET EmulatorSessionCoordinator and PinnedEmulatorSession" "Phase 04" {
                    properties {
                        "Requirements" "FR-RETRO-010, TR-RETRO-ADAPTER-010, TEST-RETRO-010"
                        "Verification boundary" "Phase 04 fixture adapters. Shipped providers are assigned to Phase 05."
                    }
                }
                sessionControl = component "Typed Session Control" "Checks captured identity, grants, capabilities, revisions, and payloads before routing to advertised ports. Correlates exact retries, operation receipts, and uncertain outcomes." ".NET EmulatorSessionControl" "Phase 04"
                sessionSubscriptions = component "Session Subscriptions" "Reserves provider subscriptions, tracks ordered control cursors and gaps, and buffers media independently. Drains pending and paused subscriptions before the provider connection closes." ".NET EmulatorSubscriptions and BufferedEmulatorMedia" "Phase 04"
                machineContract = component "Machine Descriptor and Memory Map" "Validates the session's descriptor schema, CPU and address spaces. Watches ordered map revisions, rechecks current capability and map scope before mapped operations, and retains independently advertised raw inspection." ".NET IMachineDescriptorPort and MachineMap" "Phase 04" {
                    properties {
                        "Requirements" "FR-RETRO-017, TR-RETRO-MACHINE-017, TEST-RETRO-017"
                        "Verification boundary" "Shared contract and live fixture map changes. Shipped descriptors are Phase 05 and selected-target presentation is Phase 06."
                    }
                }
                bundleProfiles = component "Verified Package Profiles" "Checks each declared debugger profile's reference, schema, path, and digest. Projects available profile references into project selection and resolves the active descriptor's reference under its pinned package lease." ".NET declarative profile validation" "Phase 04" {
                    properties {
                        "Requirements" "TR-RETRO-BUNDLE-009, TR-RETRO-MACHINE-017, TEST-RETRO-017"
                        "Storyboard" "STB17 installed package and verified profile selection"
                    }
                }
                emulation = component "Emulation and Replay View" "Presents video, input capture, host volume, and available frame capture. A seekable recording drives the replay playhead; live emulation shows elapsed time." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-003, TR-RETRO-AVINPUT-003"
                        "Storyboard" "STB15 Seek and adjust volume in the Emulation player"
                    }
                }
                debugger = component "Debugger and Guest Inspectors" "Displays CPU-specific registers, platform disassembly, bounded memory and bank selection, reference timing, and advertised live clock-phase or bus data. Captures the user's byte-order display choice and exposes available raw and guest inspection." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-004, TR-RETRO-DEBUG-004, FR-RETRO-016, FR-RETRO-017"
                        "Storyboards" "STB06 raw 68040 debugging; STB17 package-backed debugger"
                    }
                }
                debugTarget = component "Debugger Target Model" "Validates the active session's descriptor, CPU and map revision. Projects supporting views and bounded inspection scope. Applies Emulator default, Little-endian, or Big-endian to multibyte display values." ".NET" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-017, TR-RETRO-MACHINE-017, TEST-RETRO-017; TR-RETRO-CPU-016#ac-debugger-endian-precedence"
                    }
                }
                debugProfiles = component "CPU References and Display Profiles" "Resolves versioned register and flag definitions, opcode encodings, addressing rules, reference timing and platform display profiles for the 6502, 6809, Z80, 8080, 8086, 6800, and 68000 families. Resolves package-supplied declarative profiles to the selected version." ".NET, declarative profile data" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-016, TR-RETRO-CPU-016, TEST-RETRO-016"
                    }
                }
                artifacts = component "Media, Tests, and Diagnostics" "Presents media and snapshot workflows, test results, operation progress, and failure evidence with adapter-provided identity and compatibility." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-005, TR-RETRO-WORKFLOW-005"
                    }
                }
                agentActivity = component "Agent Session and Activity" "Presents agent launch and attach commands, scoped operations, denials, transport failures, and reattachment state." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-006, TR-RETRO-AGENT-006"
                    }
                }
                agentGateway = component "Agent Gateway" "Handles configured process launch and attach for authorized agent workflows." ".NET and MCP" "Planned"
                mcpHost = component "Retro IDE MCP Host" "Exposes registered IDE tools through the shared CQRS dispatcher. Generates schemas from public ViewModel contracts, verifies live context and grants, and returns scoped operation handles for supported background calls including package refresh." "Official MCP C# SDK, Microsoft.Extensions.Hosting, ASP.NET Core" "Phase 01,Phase 02,Phase 03,Phase 04" {
                    properties {
                        "Requirements" "FR-RETRO-015, TR-RETRO-MCP-015"
                    }
                }
                operationTracker = component "Scoped Background Operations" "Tracks accepted and running work, cancellation, and typed success or failure. Checks the original actor grant before reading, cancelling, or returning an operation result." ".NET WorkbenchOperations" "Phase 03" {
                    properties {
                        "Requirements" "FR-RETRO-015, TR-RETRO-MCP-015#ac-mcp-context-and-outcomes, TEST-RETRO-015"
                    }
                }
                emulators = component "Emulators Management View" "Browses buckets, compatible releases, and installed versions. Shows project version history, binding readiness, and verified profiles. Refreshes after UI or MCP changes and discards results for superseded project selections." "Avalonia EmulatorsPane and EmulatorsViewModel" "Phase 04" {
                    properties {
                        "Requirements" "FR-RETRO-009, TR-RETRO-BUNDLE-009, TEST-RETRO-009, FR-RETRO-015"
                    }
                }
                bundleManager = component "Emulator Bundle Manager" "Reads Git bucket manifests at a recorded commit, selects a compatible host target, verifies archives and entry points, and publishes immutable versions with receipts. Persists project pins and rollback history." ".NET EmulatorWorkspace, Git, JSON, SHA-256" "Phase 04"
                catalog = component "Pinned Adapter Resolver" "Resolves the project's selected installed bundle for the host and contract version. Checks the installation receipt and binding entry point, exposes verified profile references, and supplies a lease for session open." ".NET EmulatorWorkspace and EmulatorBundleStore" "Phase 04" {
                    properties {
                        "Requirements" "TR-RETRO-BUNDLE-009, TR-RETRO-ADAPTER-010, TR-RETRO-MACHINE-017"
                        "Package metadata" "Optional debuggerProfiles entries point to versioned declarative resources in the verified package. The active machine, map revision, and telemetry capabilities come from the session adapter."
                    }
                }
                uaeAdapter = component "UAE Adapter" "Translates UAE40 profile and session operations, supplies the active 68000-family machine description where supported, and exposes UAE-specific debugger extensions." ".NET; desktop-facing transport pending" "Planned"
                viceAdapter = component "VICE Adapter" "Launches x64sc, maps debugger operations through its loopback binary monitor, and normalizes available C64 machine, CPU and memory-bank metadata." "Process and VICE binary monitor" "Planned"
                viceSharpAdapter = component "ViceSharp Adapter" "Attaches to the configured host and maps available session metadata, debugger descriptions, monitor, media, snapshot, and diagnostics services." "ViceSharp gRPC contracts" "Planned"
            }

            workspaceFiles = container "Local Workspace Settings" "Stores IDE layout, workspace roots and Explorer state, preferences, language server registrations, and configured emulator buckets." "Local files" "DataStore,Phase 01,Phase 02,Phase 03,Phase 04"
            workspaceContent = container "Workspace Project and Folder Files" "Contains selected projects, source files, artifacts, Git metadata, and portable emulator pins and history in each project's .retroide/emulator.json." "Local filesystem" "DataStore"
            bundleStore = container "Local Emulator Bundle Store" "Holds verified, versioned installations, bindings, optional declarative debugger profiles, installation receipts, and cached bucket metadata. Active leases prevent removal." "Per-user filesystem and file leases" "DataStore,Phase 04"
            mcpStdio = container "MCP Stdio Bridge" "Launched by CLI clients to attach MCP tools to the selected running Retro IDE instance." ".NET, ModelContextProtocol" "Implemented"
        }

        uae = softwareSystem "UAE Developer" "Owns the UAE40 profile, control service, isolated worker and UAE-specific protocol and diagnostics. The desktop-facing interface is pending." "External" {
            url "https://github.com/sharpninja/uae-developer"
        }
        vice = softwareSystem "VICE" "External x64sc process and Commodore 64 runtime. Its binary monitor is the documented debugger interface." "External" {
            url "https://vice-emu.sourceforge.io/vice_2.html"
        }
        viceSharp = softwareSystem "ViceSharp" "External .NET Commodore emulator host with published gRPC contracts." "External" {
            url "https://github.com/sharpninja/vice-sharp"
        }
        buildTools = softwareSystem "Project Build Toolchain" "Operator-configured compiler, assembler, or build task that produces artifacts and diagnostics for the current project." "External"
        aiAgent = softwareSystem "Configured CLI or Desktop Agent" "Uses available scoped MCP resources and commands; its lifetime is independent of the emulator session." "External"
        buckets = softwareSystem "Emulator Bucket Repositories" "Configured Git sources publish per-emulator JSON manifests pointing to versioned release archives." "External"
        emulatorReleases = softwareSystem "Emulator Release Artifact Hosting" "Hosts the versioned platform archives referenced by the bucket manifests." "External"
        gitRemotes = softwareSystem "Git Remotes" "Hosts workspace repositories for clone, fetch, pull, and push using the user's configured Git authentication." "External"
        languageServers = softwareSystem "Configured Language Servers" "Installed processes such as clangd and rust-analyzer that supply language analysis through negotiated LSP features." "External"
        nativeShell = softwareSystem "Native Repository Shell" "Runs the user's interactive shell with the inherited environment and the selected Git worktree as its current directory." "External"
        fixtureAdapter = softwareSystem "Adapter Conformance Fixture" "Supplies a fixture binding factory and typed provider ports for Phase 04 contract verification." "External,Test Fixture"
        qbrain = softwareSystem "QBrain Retro IDE Workspace" "Owns the Retro IDE functional requirements and their linked technical requirements and verification records." "External"

        developer -> retro.desktop.shell "Works with projects, documents, and docked tools"
        developer -> retro.desktop.emulation "Views and operates the selected Emulation tab"
        developer -> retro.desktop.debugger "Inspects the selected CPU, memory and timing, and sets the display byte order"
        developer -> retro.desktop.sourceEditor "Edits source in the selected document tab"
        developer -> retro.desktop.emulators "Manages emulator packages and chooses a verified version for a project"
        developer -> qbrain "Reviews IDE requirements and traceability" "Project workflow"

        retro.desktop.shell -> retro.desktop.commands "Reads descriptors and binds menus, command bars, and shortcuts to the same predefined command" "ICommand"
        retro.desktop.shell -> retro.desktop.ideState "Observes logical interaction blocks and presents the native wait cursor over affected UI"
        retro.desktop.shell -> retro.desktop.localizer "Resolves labels, accessible names, and icon slugs for rendering"
        retro.desktop.localizer -> retro.desktop.ideState "Observes the current presentation culture"
        retro.desktop.localizer -> retro.desktop.localeCatalog "Finds typed resources using the locale fallback chain"
        retro.desktop.localizer -> retro.desktop.shell "Notifies open windows to refresh localized presentation"
        retro.desktop.shell -> retro.desktop.viewModels "Binds to the registered workspace, document, and tool ViewModels" "Avalonia bindings"
        retro.desktop.shell -> retro.desktop.documentViews "Binds the active editor tab and its scoped command instances" "Avalonia tabbed MDI"
        retro.desktop.shell -> retro.desktop.sourceEditor "Hosts the AvaloniaEdit control in each source tab"
        retro.desktop.shell -> retro.desktop.workspaceExplorer "Opens the dockable workspace project and folder tree"
        retro.desktop.workspaceExplorer -> retro.desktop.viewModels "Binds roots, expansion, selection and tree requests through its registered IView"
        retro.desktop.controller -> retro.desktop.workspaceWatcher "Coordinates watching for the current workspace roots and stops watching on close"
        retro.desktop.workspaceWatcher -> retro.workspaceContent "Receives external file changes and monitors root availability"
        retro.desktop.workspaceWatcher -> retro.desktop.uiDispatcher "Posts affected directories and root availability to Avalonia"
        retro.desktop.uiDispatcher -> retro.desktop.workspaceExplorer "Refreshes only expanded affected branches for the live workspace"
        retro.desktop.workspaceExplorer -> retro.workspaceFiles "Restores roots, expansion and selection across workbench reopen"
        retro.desktop.shell -> retro.desktop.sourceControl "Opens the dockable Git Source Control pane"
        retro.desktop.sourceControl -> retro.desktop.commands "Binds Git commands for the selected repository and worktree"
        retro.desktop.sourceEditor -> retro.desktop.documentViews "Binds the document buffer and source location, and raises scoped editor commands"
        retro.desktop.viewModels -> retro.desktop.controller "Register directly and raise CommandRequested with the request ID and captured context, or request an explicit query" "IView events"
        retro.desktop.controller -> retro.desktop.viewModels "Owns view registrations and attaches or detaches the command request handler"
        retro.desktop.documentViews -> retro.desktop.documentController "Register directly and raise document command requests with the captured document ID" "IView events"
        retro.desktop.documentController -> retro.desktop.documentViews "Owns editor view registrations and attaches or detaches the command request handler"
        retro.desktop.documentController -> retro.desktop.controller "Executes the selected typed document request through the shared CQRS entry point"
        retro.desktop.controller -> retro.desktop.uiDispatcher "Schedules delivery for the request and originating registration"
        retro.desktop.documentController -> retro.desktop.uiDispatcher "Schedules document results for the originating editor registration"
        retro.desktop.uiDispatcher -> retro.desktop.viewModels "Invokes the result callback on the original registered view on the UI thread"
        retro.desktop.uiDispatcher -> retro.desktop.documentViews "Invokes the document owner's result callback on the original editor view"
        retro.desktop.viewModels -> retro.desktop.ideState "Observe model state and expose it to the view"
        retro.desktop.documentViews -> retro.desktop.ideState "Project document state into editor tabs"
        retro.desktop.commands -> retro.desktop.ideState "Reads handler-published eligibility and invalidates CanExecute when it changes"
        retro.desktop.commands -> retro.desktop.viewModels "Raises CommandRequested on the originating view and awaits asynchronous completion" "RelayCommand or AsyncRelayCommand"
        retro.desktop.commands -> retro.desktop.documentViews "Raises the scoped document command on the originating editor tab" "RelayCommand or AsyncRelayCommand"
        retro.desktop.controller -> retro.desktop.unitOfWork "Dispatches a typed command or query in its captured workspace context"
        retro.desktop.unitOfWork -> retro.desktop.ideState "Reads state for queries; commands publish progress, interaction blocks, and outcomes"
        retro.desktop.unitOfWork -> retro.workspaceFiles "Uses local layout and Explorer persistence and the saved display language" "Local files"
        retro.desktop.unitOfWork -> retro.workspaceContent "Resolves workspace roots, enumerates directories, and opens the selected files" "Local filesystem"
        retro.desktop.unitOfWork -> retro.desktop.launch "Invokes build and debug-launch handlers"
        retro.desktop.unitOfWork -> retro.desktop.sessions "Invokes session, debugger, media, and diagnostics handlers"
        retro.desktop.unitOfWork -> retro.desktop.debugTarget "Validates captured CPU and memory-map scope, and applies presentation changes"
        retro.desktop.unitOfWork -> retro.desktop.agentGateway "Invokes configured agent launch or attach"
        retro.desktop.unitOfWork -> retro.desktop.bundleManager "Invokes repository refresh and emulator bundle lifecycle handlers"
        retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Dispatches typed Git requests for the captured repository and worktree"
        retro.desktop.unitOfWork -> retro.desktop.documentPublication "Stages source text and waits before publishing the captured save"
        retro.desktop.documentPublication -> retro.desktop.mutationGate "Waits to publish while a reviewed Git write holds the gate"
        retro.desktop.documentPublication -> retro.workspaceContent "Atomically replaces the source file under the workspace root"
        retro.desktop.documentController -> retro.desktop.documentPublication "Commits the staged save after checking registration and save revision"
        retro.desktop.documentController -> retro.desktop.languageServices "Queues the saved document and exact committed text after publication"
        retro.desktop.unitOfWork -> retro.desktop.languageServices "Dispatches language queries and document synchronization for the captured editor context"
        retro.desktop.languageServices -> retro.workspaceFiles "Resolves configured server registration and workspace language settings"
        retro.desktop.languageServices -> languageServers "Starts and manages server processes, synchronizes documents, and sends negotiated LSP requests" "JSON-RPC over stdio"
        languageServers -> retro.desktop.languageServices "Returns results and sends diagnostics and workspace requests" "JSON-RPC over stdio"
        retro.desktop.languageServices -> retro.desktop.controller "Routes typed server-originated edits and diagnostics through the main MVC controller"
        retro.desktop.unitOfWork -> retro.desktop.terminal "Opens an interactive terminal for the selected workspace context"
        retro.desktop.gitIntegration -> retro.workspaceContent "Reads or updates the selected worktree, index, refs, and Git objects"
        retro.desktop.gitIntegration -> retro.desktop.gitMutationLease "Requests a validated lease for a write to an existing checkout"
        retro.desktop.gitMutationLease -> retro.desktop.mutationGate "Waits for exclusive publication in the IDE process"
        retro.desktop.gitMutationLease -> retro.workspaceContent "Rediscovers the captured checkout and compares native Git path identity"
        retro.desktop.gitMutationLease -> retro.desktop.gitIntegration "Returns the validated lease or reports that the captured checkout is unavailable"
        retro.desktop.gitIntegration -> retro.desktop.mutationGate "Holds repository setup through installation or cleanup"
        retro.desktop.gitIntegration -> gitRemotes "Uses native Git for repository transfer and remote updates" "HTTPS or SSH"
        retro.desktop.terminal -> retro.workspaceContent "Revalidates the captured worktree before opening its shell"
        retro.desktop.terminal -> nativeShell "Starts the shell in the verified worktree and sends input and resize requests" "Native PTY"
        nativeShell -> retro.desktop.terminal "Streams native output and reports process exit" "Native PTY"
        retro.desktop.terminal -> retro.desktop.ideState "Publishes terminal attachment, transcript slices, and exit state"
        retro.desktop.sessions -> retro.desktop.ideState "Publishes normalized session state and capability changes"
        retro.desktop.sessions -> retro.desktop.debugTarget "Publishes the scoped machine descriptor, negotiated timing capabilities, and ordered memory-map revisions"
        retro.desktop.debugTarget -> retro.desktop.debugProfiles "Resolves the selected CPU variant, reference timing, and platform presentation profile"
        retro.desktop.debugTarget -> retro.workspaceFiles "Reads and persists the user's debugger display byte order"
        retro.desktop.debugTarget -> retro.desktop.ideState "Publishes the resolved selected-target debugger state, effective display order, and safe raw fallback"
        retro.desktop.debugProfiles -> retro.bundleStore "Reads optional declarative display profiles from the leased, verified package version"
        retro.desktop.sessions -> retro.desktop.emulation "Supplies negotiated media streams independently of control traffic"
        retro.desktop.shell -> retro.desktop.emulation "Opens the Emulation document"
        retro.desktop.shell -> retro.desktop.debugger "Opens debugging and console tools"
        retro.desktop.shell -> retro.desktop.artifacts "Opens media, snapshot, test, and diagnostic panels"
        retro.desktop.shell -> retro.desktop.agentActivity "Opens the scoped agent activity view"
        retro.desktop.shell -> retro.desktop.emulators "Opens the Emulators manager"
        retro.desktop.emulators -> retro.desktop.commands "Binds bundle management actions to registered commands"
        retro.desktop.emulators -> retro.desktop.controller "Raises typed package requests from its registered view"
        retro.desktop.controller -> retro.desktop.emulators "Returns the outcome to the originating pane and notifies the affected live registration"

        retro.desktop.launch -> retro.workspaceFiles "Loads project configuration and target presets" "Local files"
        retro.desktop.launch -> buildTools "Runs the configured build and collects diagnostics and outputs" "Configured process"
        retro.desktop.launch -> retro.desktop.sessions "Requests launch or attach using resolved artifacts"
        retro.desktop.emulation -> retro.desktop.viewModels "Binds registered views for selected-session media and input requests"
        retro.desktop.debugger -> retro.desktop.viewModels "Binds registered views for debugger queries and commands"
        retro.desktop.debugger -> retro.desktop.commands "Invokes scoped bank selection and byte-order presentation commands"
        retro.desktop.artifacts -> retro.desktop.viewModels "Binds registered views for media, snapshot, test, and diagnostic requests"
        retro.desktop.agentActivity -> retro.desktop.viewModels "Binds registered views for scoped agent requests"
        retro.desktop.agentGateway -> aiAgent "Starts or attaches a configured scoped agent" "Configured process and MCP stdio where supported"
        aiAgent -> retro.desktop.mcpHost "Discovers and invokes IDE tools on the selected workbench" "MCP Streamable HTTP on loopback"
        aiAgent -> retro.mcpStdio "Discovers and invokes IDE tools through the CLI attachment" "MCP stdio"
        retro.mcpStdio -> retro.desktop.mcpHost "Attaches to the same registered IDE instance with the client's granted context"
        retro.desktop.mcpHost -> retro.desktop.commands "Builds tool schemas for the registered IDE features"
        retro.desktop.mcpHost -> retro.desktop.viewModels "Uses the declared ViewModel context contracts for scoped tool requests"
        retro.desktop.mcpHost -> retro.desktop.documentViews "Reads and resolves the public editor ViewModel context snapshot"
        retro.desktop.mcpHost -> retro.desktop.controller "Submits the authorized typed request with workspace and target context"
        retro.desktop.controller -> retro.desktop.mcpHost "Returns the typed result for the originating tool invocation"
        retro.desktop.mcpHost -> aiAgent "Returns structured tool results and supported progress" "MCP"
        retro.desktop.mcpHost -> retro.mcpStdio "Returns the invocation result to the attached bridge" "MCP Streamable HTTP on loopback"
        retro.mcpStdio -> aiAgent "Returns structured tool results to the requesting CLI client" "MCP stdio"
        retro.desktop.unitOfWork -> retro.desktop.operationTracker "Starts a supported background operation or handles an authorized status, result, or cancellation request"
        retro.desktop.operationTracker -> retro.desktop.controller "Runs the captured work through the same dispatcher with its operation and grant lifetime"
        retro.desktop.operationTracker -> retro.desktop.unitOfWork "Supplies the scoped operation handle, status, or typed outcome"
        aiAgent -> uae "Uses the scoped UAE MCP boundary when available" "Planned MCP"

        retro.desktop.sessions -> retro.desktop.catalog "Resolves the selected adapter and invokes normalized operations"
        retro.desktop.catalog -> retro.desktop.sessions "Supplies the verified binding and installed-version lease"
        retro.desktop.sessions -> retro.desktop.sessionControl "Creates scoped control for the validated session identity"
        retro.desktop.sessions -> retro.desktop.sessionSubscriptions "Drains active subscriptions before closing the connection and releasing its lease"
        retro.desktop.sessions -> retro.bundleStore "Retains the installed-version lease until provider cleanup completes"
        retro.desktop.sessions -> retro.desktop.bundleProfiles "Resolves the current descriptor's profile against the session lease"
        retro.desktop.sessionControl -> retro.desktop.sessionSubscriptions "Opens scoped control events and independent bounded media"
        retro.desktop.sessionControl -> retro.desktop.machineContract "Validates descriptor observations and mapped access before adapter entry"
        retro.desktop.sessionSubscriptions -> retro.desktop.machineContract "Supplies ordered map changes and event gaps"
        retro.desktop.sessions -> fixtureAdapter "Negotiates a fixture binding, checks initial state, and disposes the connection" "Phase 04 conformance only"
        fixtureAdapter -> retro.desktop.sessions "Returns compatible fixture identity, close policy, and initial state"
        retro.desktop.sessionControl -> fixtureAdapter "Dispatches scoped requests to advertised typed fixture ports" "Phase 04 conformance only"
        fixtureAdapter -> retro.desktop.sessionControl "Returns typed results, including revision conflicts and reports for outcome reconciliation"
        retro.desktop.sessionSubscriptions -> fixtureAdapter "Subscribes through separately negotiated control and media ports, and drains each subscription during teardown" "Phase 04 conformance only"
        fixtureAdapter -> retro.desktop.sessionSubscriptions "Publishes ordered control events and independent media items"
        retro.desktop.machineContract -> fixtureAdapter "Requests the current descriptor through its advertised typed port" "Phase 04 conformance only"
        fixtureAdapter -> retro.desktop.machineContract "Returns the session descriptor, profile reference, and map revision or a typed unavailable result"
        retro.desktop.catalog -> retro.desktop.bundleProfiles "Inspects profile references for the selected installed version"
        retro.desktop.bundleProfiles -> retro.bundleStore "Verifies a declarative profile against its manifest and installed assets"
        retro.desktop.bundleManager -> buckets "Fetches configured Git sources and reads versioned manifests" "Git and JSON"
        retro.desktop.bundleManager -> emulatorReleases "Downloads the archive named for the compatible host target" "HTTPS"
        retro.desktop.bundleManager -> retro.bundleStore "Installs verified binaries, bindings, optional debugger profile data and receipts atomically"
        retro.desktop.bundleManager -> retro.workspaceFiles "Reads and stores configured Git bucket sources"
        retro.desktop.bundleManager -> retro.workspaceContent "Writes a verified project pin and its rollback history"
        retro.desktop.bundleManager -> retro.desktop.catalog "Projects binding readiness and verified profiles for the current project"
        retro.desktop.catalog -> retro.workspaceContent "Reads the exact project pin and manifest revision"
        retro.desktop.catalog -> retro.bundleStore "Resolves verified local entry points and holds the selected version for the session"
        retro.desktop.catalog -> retro.desktop.uaeAdapter "Resolves UAE presets"
        retro.desktop.catalog -> retro.desktop.viceAdapter "Resolves VICE presets"
        retro.desktop.catalog -> retro.desktop.viceSharpAdapter "Resolves ViceSharp presets"
        retro.desktop.viceSharpAdapter -> retro.desktop.catalog "Normalizes session state, command outcomes, events, and media streams"
        retro.desktop.uaeAdapter -> uae "Launches or attaches through UAE control when a desktop-facing bridge is available" "Planned UAE control bridge"
        retro.desktop.viceAdapter -> vice "Starts x64sc and exchanges supported debugger commands and events" "Local process and loopback binary monitor"
        retro.desktop.viceSharpAdapter -> viceSharp "Attaches to a configured host and uses its published session services" "gRPC"
        viceSharp -> retro.desktop.viceSharpAdapter "Returns session data and streams available events and frames" "gRPC"

        windows = deploymentEnvironment "Windows Development" {
            deploymentNode "Developer Workstation" "Verified Windows workbench, native terminal, and language services. Emulator hosts are planned alternatives." "Windows" {
                containerInstance retro.desktop
                containerInstance retro.mcpStdio
                containerInstance retro.workspaceFiles
                containerInstance retro.workspaceContent
                containerInstance retro.bundleStore
                softwareSystemInstance uae
                softwareSystemInstance vice
                softwareSystemInstance viceSharp
                softwareSystemInstance languageServers
                softwareSystemInstance nativeShell
            }
        }
        linux = deploymentEnvironment "Linux Development" {
            deploymentNode "Developer Workstation" "Verified Linux workbench, native terminal, and language services. Emulator hosts are planned alternatives." "Linux" {
                containerInstance retro.desktop
                containerInstance retro.mcpStdio
                containerInstance retro.workspaceFiles
                containerInstance retro.workspaceContent
                containerInstance retro.bundleStore
                softwareSystemInstance uae
                softwareSystemInstance vice
                softwareSystemInstance viceSharp
                softwareSystemInstance languageServers
                softwareSystemInstance nativeShell
            }
        }
    }

    views {
        systemContext retro "SystemContext" "Retro IDE, its developer, emulator hosts, build tools, and agent boundary" {
            include *
            exclude fixtureAdapter
            include qbrain
            autoLayout lr
        }
        container retro "Containers" "Retro IDE desktop and workspace settings, with external emulator boundaries" {
            include *
            exclude fixtureAdapter
            autoLayout lr
        }
        component retro.desktop "DesktopComponents" "IDE responsibilities in the desktop container" {
            include developer retro.desktop.shell retro.desktop.commands retro.desktop.localizer retro.desktop.localeCatalog retro.desktop.controller retro.desktop.documentController retro.desktop.unitOfWork retro.desktop.viewModels retro.desktop.documentViews retro.desktop.sourceEditor retro.desktop.workspaceExplorer retro.desktop.workspaceWatcher retro.desktop.sourceControl retro.desktop.gitIntegration retro.desktop.gitMutationLease retro.desktop.mutationGate retro.desktop.documentPublication retro.desktop.terminal retro.desktop.languageServices retro.desktop.uiDispatcher retro.desktop.ideState retro.desktop.launch retro.desktop.sessions retro.desktop.sessionControl retro.desktop.sessionSubscriptions retro.desktop.machineContract retro.desktop.bundleProfiles retro.desktop.emulation retro.desktop.debugger retro.desktop.debugTarget retro.desktop.debugProfiles retro.desktop.artifacts retro.desktop.agentActivity retro.desktop.agentGateway retro.desktop.mcpHost retro.desktop.operationTracker retro.desktop.emulators retro.desktop.bundleManager retro.desktop.catalog retro.workspaceFiles retro.workspaceContent retro.bundleStore retro.mcpStdio buildTools aiAgent buckets emulatorReleases gitRemotes languageServers nativeShell
            autoLayout lr
        }
        component retro.desktop "Phase03WorkflowComponents" "Accepted Git, terminal, editor publication, language server, and MCP operation boundaries" {
            include developer retro.desktop.sourceControl retro.desktop.documentController retro.desktop.controller retro.desktop.unitOfWork retro.desktop.gitIntegration retro.desktop.gitMutationLease retro.desktop.mutationGate retro.desktop.documentPublication retro.desktop.terminal retro.desktop.languageServices retro.desktop.mcpHost retro.desktop.operationTracker retro.workspaceContent retro.workspaceFiles retro.mcpStdio gitRemotes nativeShell languageServers aiAgent
            autoLayout lr
        }
        component retro.desktop "Phase04WorkflowComponents" "Accepted package management, shared emulator contracts, and their provider conformance fixture" {
            include developer retro.desktop.emulators retro.desktop.controller retro.desktop.unitOfWork retro.desktop.bundleManager retro.desktop.catalog retro.desktop.sessions retro.desktop.sessionControl retro.desktop.sessionSubscriptions retro.desktop.machineContract retro.desktop.bundleProfiles retro.desktop.mcpHost retro.desktop.operationTracker retro.workspaceFiles retro.workspaceContent retro.bundleStore retro.mcpStdio aiAgent buckets emulatorReleases fixtureAdapter
            autoLayout lr
        }
        component retro.desktop "AdapterComponents" "Planned Phase 05 provider integration using the shared pinned bundle and session contracts" {
            include retro.desktop.launch retro.desktop.sessions retro.desktop.catalog retro.desktop.uaeAdapter retro.desktop.viceAdapter retro.desktop.viceSharpAdapter retro.desktop.debugTarget retro.workspaceFiles retro.bundleStore uae vice viceSharp
            autoLayout lr
        }
        component retro.desktop "PackageDebuggerComponents" "Accepted package and machine contracts with planned Phase 06 debugger presentation" {
            include developer retro.desktop.emulators retro.desktop.bundleManager retro.bundleStore retro.desktop.catalog retro.desktop.sessions retro.desktop.machineContract retro.desktop.bundleProfiles retro.desktop.debugTarget retro.desktop.debugProfiles retro.workspaceFiles retro.desktop.ideState retro.desktop.viewModels retro.desktop.debugger
            autoLayout lr
        }
        dynamic retro.desktop "CommandDispatch" "A Toolkit command raises an event on its registered view and the controller returns the CQRS result to that view through Avalonia" {
            developer -> retro.desktop.shell "Choose Build Project from the menu, from the command bar, or with its shortcut"
            retro.desktop.shell -> retro.desktop.commands "Execute the shared Build Project AsyncRelayCommand"
            retro.desktop.commands -> retro.desktop.viewModels "Raise CommandRequested on the originating registered view"
            retro.desktop.viewModels -> retro.desktop.controller "Deliver request ID, ViewKey, and captured context to the registered subscriber"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Build Project request"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Recheck context and publish the pending operation"
            retro.desktop.unitOfWork -> retro.desktop.launch "Run the registered build-only handler"
            retro.desktop.launch -> buildTools "Run the configured build and collect diagnostics"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish completion or source diagnostics"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the correlated result for the originating registration"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Apply the result on the UI thread if the originating view is still registered"
            retro.desktop.viewModels -> retro.desktop.ideState "Observe the updated build output and diagnostics"
            retro.desktop.shell -> retro.desktop.viewModels "Render updated observable properties"
            autoLayout lr
        }
        dynamic retro.desktop "WorkbenchFocusParity" "Phase 01 shared tool-window focus through the workbench, stdio bridge, or Streamable HTTP" {
            developer -> retro.desktop.shell "Invoke Focus tool pane using the menu, command bar, or shortcut"
            retro.desktop.shell -> retro.desktop.commands "Execute the shared workbench.view.focus Toolkit command"
            retro.desktop.commands -> retro.desktop.viewModels "Capture tool ID and live public workbench context"
            retro.desktop.viewModels -> retro.desktop.controller "Raise CommandRequested on the registered view"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed focus command"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Focus the eligible dockable and publish the new revision"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the correlated result"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver to the originating live registration"
            aiAgent -> retro.mcpStdio "Attach a CLI client with the selected workbench descriptor"
            retro.mcpStdio -> retro.desktop.mcpHost "Forward discovery and calls over authenticated loopback Streamable HTTP"
            aiAgent -> retro.desktop.mcpHost "A desktop client may invoke the same tools directly over Streamable HTTP"
            retro.desktop.mcpHost -> retro.desktop.commands "Discover the same focus command and its public context schema"
            retro.desktop.mcpHost -> retro.desktop.controller "Submit workbench_focus_tool with the captured ViewModel context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Validate scope, live registration and revision on the UI thread"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Apply the same Dock focus operation and publish the new revision"
            retro.desktop.controller -> retro.desktop.mcpHost "Return the correlated outcome and new public context snapshot"
            retro.desktop.mcpHost -> aiAgent "Return the structured tool result to the requesting client"
            autoLayout lr
        }
        dynamic retro.desktop "McpToolDispatch" "An agent reads a ViewModel context, then invokes the same typed editor operation used by the workbench" {
            aiAgent -> retro.desktop.mcpHost "Discover supported IDE tools in the selected client scope"
            retro.desktop.mcpHost -> retro.desktop.commands "Read the registered editor operation and its tool schema"
            retro.desktop.mcpHost -> retro.desktop.documentViews "Read the public editor ViewModel context for the authorized client"
            retro.desktop.mcpHost -> aiAgent "Return the workspace, view key, document ID, and current revision"
            aiAgent -> retro.desktop.mcpHost "Call editor_edit_text with the workbench and editor contexts and captured revision"
            retro.desktop.mcpHost -> retro.desktop.documentViews "Resolve the live registration and recheck the expected revision"
            retro.desktop.mcpHost -> retro.desktop.controller "Validate client scope and submit the typed edit request"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch to the shared document edit handler"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Apply the validated edits and publish the resulting document state"
            retro.desktop.documentViews -> retro.desktop.ideState "Observe the updated document in the attached editor"
            retro.desktop.controller -> retro.desktop.mcpHost "Return the outcome and new revision to the calling client"
            retro.desktop.mcpHost -> aiAgent "Deliver the structured MCP tool result"
            autoLayout lr
        }
        dynamic retro.desktop "McpOperationLifecycle" "An authorized client starts a background Git operation and reads its typed outcome across transports" {
            aiAgent -> retro.desktop.mcpHost "Invoke a registered Git tool with captured context and background enabled"
            retro.desktop.mcpHost -> retro.desktop.controller "Submit the typed request with the caller's grant"
            retro.desktop.controller -> retro.desktop.unitOfWork "Check the captured workbench context and background eligibility"
            retro.desktop.unitOfWork -> retro.desktop.operationTracker "Register the operation with its original actor grant"
            retro.desktop.operationTracker -> retro.desktop.unitOfWork "Return the operation handle"
            retro.desktop.controller -> retro.desktop.mcpHost "Return the accepted handle"
            retro.desktop.mcpHost -> aiAgent "Return the typed operation handle over HTTP"
            retro.desktop.operationTracker -> retro.desktop.controller "Dispatch the operation with its linked cancellation token"
            retro.desktop.controller -> retro.desktop.unitOfWork "Run the same typed Git handler used by the workbench"
            retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Run the reviewed Git operation"
            aiAgent -> retro.mcpStdio "Request operation status or cancellation, then request the result"
            retro.mcpStdio -> retro.desktop.mcpHost "Forward the scoped calls to the same live IDE"
            retro.desktop.mcpHost -> retro.desktop.controller "Submit the authorized operation-handle calls"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the operation-handle request"
            retro.desktop.unitOfWork -> retro.desktop.operationTracker "Check the original grant for status, cancellation, or result"
            retro.desktop.operationTracker -> retro.desktop.unitOfWork "Return the status or typed success, failure, or cancellation"
            retro.desktop.controller -> retro.desktop.mcpHost "Return the scoped result or a stable handle-unavailable code"
            retro.desktop.mcpHost -> retro.mcpStdio "Send the typed reply to the requesting bridge"
            retro.mcpStdio -> aiAgent "Deliver the response over stdio"
            autoLayout lr
        }
        dynamic retro.desktop "DocumentMdiSave" "The controller that the editor tab registers with owns its Save command and result delivery" {
            retro.desktop.documentViews -> retro.desktop.documentController "Register the editor tab directly by ViewKey"
            retro.desktop.documentController -> retro.desktop.documentViews "Subscribe as this view's dispatch owner"
            developer -> retro.desktop.shell "Invoke Save for the active editor tab"
            retro.desktop.shell -> retro.desktop.commands "Execute the shared Save command for that tab"
            retro.desktop.commands -> retro.desktop.documentViews "Raise CommandRequested on the editor tab with its captured document ID"
            retro.desktop.documentViews -> retro.desktop.documentController "Deliver the request to the controller the tab registered with"
            retro.desktop.documentController -> retro.desktop.controller "Execute the typed Save Document request through the shared CQRS entry point"
            retro.desktop.controller -> retro.desktop.unitOfWork "Invoke the registered Save Document handler"
            developer -> retro.desktop.shell "Switch to another editor tab while the save is pending"
            retro.desktop.unitOfWork -> retro.desktop.documentPublication "Stage the captured text"
            retro.desktop.documentPublication -> retro.desktop.mutationGate "Wait to publish the source file"
            retro.desktop.documentController -> retro.desktop.documentPublication "Verify the original registration and revision, then commit"
            retro.desktop.documentPublication -> retro.workspaceContent "Replace the source with the captured text"
            retro.desktop.documentController -> retro.desktop.languageServices "Queue synchronization for the committed save"
            retro.desktop.documentController -> retro.desktop.uiDispatcher "Schedule the correlated outcome for the original registration"
            retro.desktop.uiDispatcher -> retro.desktop.documentViews "Deliver the result to the same editor tab if its registration is live"
            autoLayout lr
        }
        dynamic retro.desktop "SourceEditorWorkflow" "Navigate from a build diagnostic to the source tab and edit the identified source" {
            retro.desktop.shell -> retro.desktop.artifacts "Show the build diagnostics"
            retro.desktop.artifacts -> retro.desktop.viewModels "Request navigation to the diagnostic's project file and line"
            retro.desktop.viewModels -> retro.desktop.controller "Raise the source navigation command on the registered diagnostics view"
            retro.desktop.controller -> retro.desktop.unitOfWork "Resolve the source location and dispatch the open or select request"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the selected document and source location"
            retro.desktop.documentViews -> retro.desktop.ideState "Observe the selected document and location"
            retro.desktop.shell -> retro.desktop.sourceEditor "Open or select the editor tab"
            retro.desktop.sourceEditor -> retro.desktop.documentViews "Reveal the file and source range in AvaloniaEdit"
            developer -> retro.desktop.sourceEditor "Edit the source in its independent tab buffer"
            autoLayout lr
        }
        dynamic retro.desktop "LanguageServerCompletion" "Request completion for the current unsaved buffer and apply a result to the originating editor" {
            developer -> retro.desktop.sourceEditor "Request completion while editing an unsaved document"
            retro.desktop.sourceEditor -> retro.desktop.documentViews "Capture the document version and caret"
            retro.desktop.documentViews -> retro.desktop.documentController "Dispatch the completion request from the registered editor"
            retro.desktop.documentController -> retro.desktop.controller "Submit the typed request through the shared CQRS entry point"
            retro.desktop.controller -> retro.desktop.unitOfWork "Invoke the language completion query"
            retro.desktop.unitOfWork -> retro.desktop.languageServices "Use the negotiated completion capability for this document"
            retro.desktop.languageServices -> languageServers "Synchronize unsaved changes and request completion"
            languageServers -> retro.desktop.languageServices "Return completion items for the correlated request"
            retro.desktop.documentController -> retro.desktop.uiDispatcher "Schedule completion for the captured editor and document version"
            retro.desktop.uiDispatcher -> retro.desktop.documentViews "Present completions in the originating editor if the version still matches"
            autoLayout lr
        }
        dynamic retro.desktop "LanguageServerDocumentSync" "Synchronize an editor document and deliver its committed save before a subsequent close" {
            retro.desktop.documentViews -> retro.desktop.documentController "Raise Save for the captured editor revision"
            retro.desktop.documentController -> retro.desktop.controller "Submit the typed save"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the authorized document request"
            retro.desktop.unitOfWork -> retro.desktop.documentPublication "Stage the exact text captured by Save"
            retro.desktop.documentPublication -> retro.desktop.mutationGate "Wait until workspace publication is available"
            retro.desktop.documentController -> retro.desktop.documentPublication "Recheck the registered editor and commit the staged file"
            retro.desktop.documentPublication -> retro.workspaceContent "Replace the source file"
            retro.desktop.documentController -> retro.desktop.languageServices "Queue the exact committed text on the session update queue"
            retro.desktop.languageServices -> languageServers "Send didOpen or didChange as needed to synchronize the document"
            retro.desktop.languageServices -> languageServers "Send negotiated didSave and exact saved text when requested"
            retro.desktop.languageServices -> languageServers "Send didClose for the tracked document after the queued save"
            autoLayout lr
        }
        dynamic retro.desktop "WorkspaceExplorerOpen" "Browse a workspace containing project and folder roots and open a source file" {
            developer -> retro.desktop.shell "Open a project or folder and use Project Explorer"
            retro.desktop.shell -> retro.desktop.workspaceExplorer "Show project and folder roots for the workspace"
            retro.desktop.workspaceExplorer -> retro.desktop.viewModels "Request the children for the expanded root"
            retro.desktop.viewModels -> retro.desktop.controller "Route the explorer query through its registered controller"
            retro.desktop.controller -> retro.desktop.unitOfWork "Read children for the captured workspace root"
            retro.desktop.unitOfWork -> retro.workspaceContent "Enumerate the selected directory"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the updated explorer branch"
            retro.desktop.workspaceExplorer -> retro.desktop.viewModels "Open the selected file under its workspace root"
            retro.desktop.viewModels -> retro.desktop.controller "Raise the Open Document command"
            retro.desktop.controller -> retro.desktop.unitOfWork "Resolve the selected document and its editor tab"
            retro.desktop.unitOfWork -> retro.workspaceContent "Open the captured project or folder file"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the new or active document tab"
            retro.desktop.shell -> retro.desktop.sourceEditor "Show the source file in the AvaloniaEdit tab"
            autoLayout lr
        }
        dynamic retro.desktop "WorkspaceExplorerRefresh" "Restore a workspace and refresh its affected expanded directories after external changes" {
            retro.desktop.workspaceExplorer -> retro.workspaceFiles "Restore roots, saved expansion and selection"
            retro.desktop.controller -> retro.desktop.workspaceWatcher "Watch the open roots for the current registration"
            retro.desktop.workspaceWatcher -> retro.workspaceContent "Observe a file create, rename or removal and detect root availability"
            retro.desktop.workspaceWatcher -> retro.desktop.uiDispatcher "Post a coalesced directory invalidation on Avalonia"
            retro.desktop.uiDispatcher -> retro.desktop.workspaceExplorer "Refresh affected expanded branches and leave collapsed branches for the next expansion"
            retro.desktop.workspaceExplorer -> retro.desktop.viewModels "Request an updated listing for the selected root and relative directory"
            retro.desktop.viewModels -> retro.desktop.controller "Submit a scoped listing from the registered workspace"
            retro.desktop.controller -> retro.desktop.unitOfWork "Check the current registration before returning directory results"
            retro.desktop.unitOfWork -> retro.workspaceContent "Enumerate the available target directory"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the current tree result and suppress retired-workspace results"
            autoLayout lr
        }
        dynamic retro.desktop "GitStageCommitPush" "Review changes and run separate Stage, Commit, and Push actions in the selected worktree" {
            developer -> retro.desktop.shell "Review changes and choose Stage for the selected hunk"
            retro.desktop.shell -> retro.desktop.sourceControl "Show the selected worktree and its changes"
            retro.desktop.sourceControl -> retro.desktop.commands "Execute the shared Stage command for the captured hunk"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the request on the registered Source Control view"
            retro.desktop.viewModels -> retro.desktop.controller "Send the staging event with repository and worktree context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Stage Hunk request"
            retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Recheck the patch preimage and stage the hunk"
            retro.desktop.gitIntegration -> retro.desktop.gitMutationLease "Hold the validated checkout lease through index publication"
            retro.desktop.gitIntegration -> retro.workspaceContent "Update the selected worktree index using Git"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Refresh the staged and unstaged changes"
            developer -> retro.desktop.shell "Review staged changes and choose Commit"
            retro.desktop.shell -> retro.desktop.sourceControl "Show the current staging and commit message"
            retro.desktop.sourceControl -> retro.desktop.commands "Execute the registered Commit command"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the captured commit request"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the commit event through the registered controller"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Commit request"
            retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Commit the selected index using native Git"
            retro.desktop.gitIntegration -> retro.desktop.gitMutationLease "Revalidate the captured checkout after the wait"
            retro.desktop.gitIntegration -> retro.workspaceContent "Create the commit and update the checked-out ref"
            developer -> retro.desktop.shell "Choose Push for the selected branch and remote"
            retro.desktop.shell -> retro.desktop.sourceControl "Show the branch, upstream, and remote"
            retro.desktop.sourceControl -> retro.desktop.commands "Execute the registered Push command"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the push request on the same registered view"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the push event with the captured branch and remote"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Push request"
            retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Invoke native Git with the captured remote and ref"
            retro.desktop.gitIntegration -> retro.desktop.gitMutationLease "Revalidate the captured checkout before connecting"
            retro.desktop.gitIntegration -> gitRemotes "Push the selected ref using configured authentication"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Refresh upstream status and publish the outcome"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the outcome for the originating Source Control view"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver the push result on the UI thread"
            autoLayout lr
        }
        dynamic retro.desktop "GitCheckoutMutation" "Reject a queued Git mutation if the selected checkout resolves to different Git metadata" {
            retro.desktop.sourceControl -> retro.desktop.commands "Invoke a reviewed branch or patch mutation for the selected checkout"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the registered command with its checkout and preimage"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the request with its original workbench context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Authorize the captured view and dispatch the typed mutation"
            retro.desktop.unitOfWork -> retro.desktop.gitIntegration "Prepare the mutation for the reviewed worktree"
            retro.desktop.gitIntegration -> retro.desktop.gitMutationLease "Acquire a validated lease before native execution"
            retro.desktop.gitMutationLease -> retro.desktop.mutationGate "Wait while a previous native operation completes"
            retro.desktop.gitMutationLease -> retro.workspaceContent "Rediscover the checkout and compare the worktree, Git directory, and common directory"
            retro.desktop.gitMutationLease -> retro.desktop.gitIntegration "Reject changed metadata with git_repository_unavailable before the native write"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the outcome for the original Source Control registration"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver the unavailable result to the originating view"
            autoLayout lr
        }
        dynamic retro.desktop "RepositoryTerminalSession" "Open and use a terminal scoped to the selected Git worktree" {
            retro.desktop.sourceControl -> retro.desktop.commands "Open Terminal in the selected worktree"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the terminal request from the registered view"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the captured workspace and checkout"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed terminal operation"
            retro.desktop.unitOfWork -> retro.desktop.terminal "Open the scoped terminal session"
            retro.desktop.terminal -> retro.workspaceContent "Check that the selected checkout still resolves to its captured Git metadata"
            retro.desktop.terminal -> nativeShell "Start the native shell with that working directory and inherited environment"
            nativeShell -> retro.desktop.terminal "Stream shell output and report process exit"
            retro.desktop.terminal -> retro.desktop.ideState "Make the bounded output and exit state available to the attached terminal"
            retro.desktop.unitOfWork -> retro.desktop.terminal "Dispatch scoped input, resize, or transcript requests"
            retro.desktop.terminal -> nativeShell "Forward input and resize requests through the PTY"
            autoLayout lr
        }
        dynamic retro.desktop "SystemWaitCursor" "A background action blocks a UI scope while Avalonia continues to process paint and input" {
            developer -> retro.desktop.shell "Invoke a command that temporarily blocks interaction in one UI region"
            retro.desktop.shell -> retro.desktop.commands "Execute the registered asynchronous command"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the command request on the originating view"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the request to its registered workbench controller"
            retro.desktop.controller -> retro.desktop.unitOfWork "Start the typed background operation"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the interaction block for the affected scope"
            retro.desktop.shell -> retro.desktop.ideState "Apply the native wait cursor to the blocked region on the UI thread"
            developer -> retro.desktop.shell "Continue using available controls while the operation runs"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Release this operation's block on completion, failure, or cancellation"
            retro.desktop.shell -> retro.desktop.ideState "Restore the normal cursor when the last block on the region clears"
            autoLayout lr
        }
        dynamic retro.desktop "EmulatorBundleInstall" "Verify and install a compatible release, then record its portable project pin" {
            developer -> retro.desktop.shell "Open Emulators to install a compatible version"
            retro.desktop.shell -> retro.desktop.emulators "Show available and installed emulator versions"
            retro.desktop.emulators -> retro.desktop.commands "Invoke Install for the selected emulator bundle"
            retro.desktop.emulators -> retro.desktop.controller "Raise Install with the selected bundle, version, and manifest commit"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed bundle installation command"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Install the selected bundle for this host"
            retro.desktop.bundleManager -> buckets "Read the JSON manifest at its recorded Git commit"
            retro.desktop.bundleManager -> emulatorReleases "Download the archive for the compatible runtime identifier"
            retro.desktop.bundleManager -> retro.bundleStore "Verify the digest, archive paths, and entry points, then atomically publish the immutable version and receipt"
            retro.desktop.controller -> retro.desktop.emulators "Deliver the install result and refresh the live pane on Avalonia"
            developer -> retro.desktop.emulators "Select the verified installed version for the current project"
            retro.desktop.emulators -> retro.desktop.controller "Raise the typed project selection request"
            retro.desktop.controller -> retro.desktop.unitOfWork "Check the captured project and dispatch the request"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Verify the installed pin and update history"
            retro.desktop.bundleManager -> retro.workspaceContent "Persist the project pin at .retroide/emulator.json"
            retro.desktop.bundleManager -> retro.desktop.catalog "Resolve binding readiness for the selected project"
            retro.desktop.catalog -> retro.bundleStore "Inspect the exact cached installation for host and contract compatibility"
            retro.desktop.catalog -> retro.desktop.bundleProfiles "Verify declared profile references for the selected installed version"
            retro.desktop.controller -> retro.desktop.emulators "Show the selected version, history, readiness, and verified profile references"
            autoLayout lr
        }
        dynamic retro.desktop "EmulatorPackageParity" "Package commands share the authorized CQRS path and the live pane across UI, HTTP, and stdio" {
            developer -> retro.desktop.emulators "Choose a compatible installed release for the current project"
            retro.desktop.emulators -> retro.desktop.controller "Raise the typed project pin selection from the registered pane"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch with the captured live project and view context"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Apply the selection through the shared package handler"
            retro.desktop.bundleManager -> retro.workspaceContent "Save the exact installed pin and history"
            retro.desktop.controller -> retro.desktop.emulators "Return the result to the pane"
            aiAgent -> retro.desktop.mcpHost "Select an installed version using the scoped HTTP tool"
            retro.desktop.mcpHost -> retro.desktop.controller "Submit the same typed request with the caller's grant"
            retro.desktop.controller -> retro.desktop.unitOfWork "Authorize the live registration and dispatch the package handler"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Apply the authorized pin change"
            aiAgent -> retro.mcpStdio "Roll back or remove through stdio"
            retro.mcpStdio -> retro.desktop.mcpHost "Forward the authorized tool call to the same workbench"
            retro.desktop.mcpHost -> retro.desktop.controller "Submit the typed rollback or removal request"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the registered operation"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Update the project pin or installed inventory"
            retro.desktop.controller -> retro.desktop.emulators "Notify the live pane on Avalonia to refresh its current project"
            retro.desktop.emulators -> retro.desktop.controller "Request a coalesced read for the current selection and ignore superseded completions"
            autoLayout lr
        }
        dynamic retro.desktop "PinnedEmulatorSessionLifecycle" "Open a pinned fixture session and keep its package leased until provider subscriptions and the connection are released" {
            retro.desktop.catalog -> retro.workspaceContent "Resolve the exact pin from the selected project"
            retro.desktop.catalog -> retro.bundleStore "Verify the cached installation and acquire its active-version lease"
            retro.desktop.catalog -> retro.desktop.sessions "Pass the leased installation and launch or attach plan"
            retro.desktop.sessions -> fixtureAdapter "Activate the fixture factory, describe versions, negotiate launch or attach, and open"
            fixtureAdapter -> retro.desktop.sessions "Return the compatible session identity, close policy, and initial state"
            retro.desktop.sessions -> retro.desktop.sessionControl "Bind advertised typed ports to the verified session"
            retro.desktop.sessionControl -> retro.desktop.sessionSubscriptions "Subscribe to control events and independent bounded media"
            retro.desktop.sessionSubscriptions -> fixtureAdapter "Open control and media subscriptions after reserving their slots"
            fixtureAdapter -> retro.desktop.sessionSubscriptions "Return sequenced events and independent media items"
            retro.desktop.sessionControl -> fixtureAdapter "Reconcile correlated operations or reconnect to the same instance as supported"
            fixtureAdapter -> retro.desktop.sessionControl "Return the correlated result and fresh state"
            retro.desktop.sessions -> retro.desktop.sessionSubscriptions "Cancel session lifetime, await pending reads, and join media pumps"
            retro.desktop.sessionSubscriptions -> fixtureAdapter "Dispose active or paused provider subscriptions during drain"
            retro.desktop.sessions -> fixtureAdapter "Dispose the connection after subscription cleanup"
            retro.desktop.sessions -> retro.bundleStore "Release the installed-version lease after connection disposal"
            autoLayout lr
        }
        dynamic retro.desktop "EmulatorSessionContract" "Planned Phase 05 provider integration and Phase 07 player consumption, with ViceSharp as an example" {
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed attach request"
            retro.desktop.unitOfWork -> retro.desktop.sessions "Open the requested session under its project context"
            retro.desktop.sessions -> retro.desktop.catalog "Load the pinned adapter and negotiate the contract version"
            retro.desktop.catalog -> retro.desktop.viceSharpAdapter "Open an attach session using its configured endpoint"
            retro.desktop.viceSharpAdapter -> viceSharp "Read host state and check supported operations"
            viceSharp -> retro.desktop.viceSharpAdapter "Return the session state and available features"
            retro.desktop.viceSharpAdapter -> retro.desktop.catalog "Normalize backend state and expose negotiated capability ports"
            retro.desktop.catalog -> retro.desktop.sessions "Return session identity, capabilities, and event cursor"
            retro.desktop.sessions -> retro.desktop.ideState "Publish state and enable supported actions"
            retro.desktop.unitOfWork -> retro.desktop.sessions "Submit a scoped execution command and observe its correlated outcome"
            retro.desktop.sessions -> retro.desktop.catalog "Invoke the advertised execution port"
            retro.desktop.catalog -> retro.desktop.viceSharpAdapter "Translate the typed execution request"
            retro.desktop.viceSharpAdapter -> viceSharp "Send the authorized gRPC request"
            viceSharp -> retro.desktop.viceSharpAdapter "Return command status and stream supported notifications"
            retro.desktop.viceSharpAdapter -> retro.desktop.catalog "Expose normalized results and sequenced control events"
            retro.desktop.catalog -> retro.desktop.sessions "Return the operation result and event updates"
            retro.desktop.sessions -> retro.desktop.ideState "Reconcile operation and state revisions"
            retro.desktop.sessions -> retro.desktop.emulation "Deliver the selected bounded video stream separately from control events"
            autoLayout lr
        }
        dynamic retro.desktop "MachineDescriptorAndProfile" "Observe a fixture machine and its map revision, then resolve the matching installed profile and scope inspection" {
            retro.desktop.sessions -> retro.desktop.sessionControl "Use the pinned session identity and advertised capability revisions"
            retro.desktop.sessionControl -> retro.desktop.machineContract "Request and validate the negotiated machine descriptor"
            retro.desktop.machineContract -> fixtureAdapter "Describe the active fixture machine on its typed port"
            fixtureAdapter -> retro.desktop.machineContract "Return CPU variants, address spaces, map revision, and the optional profile reference"
            retro.desktop.sessions -> retro.desktop.bundleProfiles "Resolve the descriptor's profile only while its identity and map revision are current"
            retro.desktop.bundleProfiles -> retro.bundleStore "Verify the reference and profile asset within the leased package version"
            fixtureAdapter -> retro.desktop.sessionSubscriptions "Report ordered map changes or current capability state"
            retro.desktop.sessionSubscriptions -> retro.desktop.machineContract "Observe the current map, recover event gaps, and invalidate a withdrawn description"
            retro.desktop.sessionControl -> retro.desktop.machineContract "Recheck identity, cursor, capabilities, range, and map revision before mapped access"
            retro.desktop.sessionControl -> fixtureAdapter "Dispatch current mapped access, or use separately advertised raw inspection during descriptor unavailability"
            fixtureAdapter -> retro.desktop.sessionControl "Return the scoped result or a stable conflict or unsupported outcome"
            autoLayout lr
        }
        dynamic retro.desktop "DebuggerMachineDescription" "Planned Phase 05 provider descriptor and Phase 06 selected-debugger presentation" {
            retro.desktop.sessions -> retro.desktop.catalog "Resolve the pinned adapter binding and negotiate the descriptor capability"
            retro.desktop.catalog -> retro.bundleStore "Load the binding from the leased, verified installed version"
            retro.desktop.catalog -> retro.desktop.viceSharpAdapter "Request the active machine of the authorized attach session"
            retro.desktop.viceSharpAdapter -> viceSharp "Read the configured CPU, machine mapping and supported observations through the backend interface"
            viceSharp -> retro.desktop.viceSharpAdapter "Return available live session data and negotiated capabilities"
            retro.desktop.viceSharpAdapter -> retro.desktop.catalog "Normalize available data into the versioned descriptor and scoped map revision"
            retro.desktop.catalog -> retro.desktop.sessions "Return the negotiated descriptor or a stable unavailable result"
            retro.desktop.sessions -> retro.desktop.debugTarget "Bind only the selected instance and apply its current map revision"
            retro.desktop.debugTarget -> retro.desktop.debugProfiles "Resolve the exact CPU variant and declared platform profile"
            retro.desktop.debugProfiles -> retro.bundleStore "Read declared profile data from the verified package if required"
            retro.desktop.debugTarget -> retro.workspaceFiles "Read the user's saved display byte-order preference"
            retro.desktop.debugTarget -> retro.desktop.ideState "Publish scoped debugger rows or an available raw view"
            retro.desktop.viewModels -> retro.desktop.ideState "Observe the target context, address limits and available operations"
            retro.desktop.debugger -> retro.desktop.viewModels "Render target-specific registers, disassembly, memory and timing"
            autoLayout lr
        }
        dynamic retro.desktop "DebuggerMemoryDisplay" "Planned Phase 06 bank inspection and saved multibyte display interpretation" {
            developer -> retro.desktop.debugger "Select an address space and bank, then choose Emulator default, Little-endian or Big-endian"
            retro.desktop.debugger -> retro.desktop.commands "Invoke the registered scoped debugger and presentation commands"
            retro.desktop.commands -> retro.desktop.viewModels "Capture the selected instance, CPU, map revision and display choice"
            retro.desktop.viewModels -> retro.desktop.controller "Send the registered view request and captured context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed inspection or preference operation"
            retro.desktop.unitOfWork -> retro.desktop.debugTarget "Recheck the selected scope and resolve the effective display order"
            retro.desktop.debugTarget -> retro.workspaceFiles "Save the display preference for later debugger views"
            retro.desktop.unitOfWork -> retro.desktop.sessions "Issue a bounded read using the selected space, bank and map revision"
            retro.desktop.sessions -> retro.desktop.ideState "Publish the correlated raw bytes with the actual map and instance identity"
            retro.desktop.debugTarget -> retro.desktop.ideState "Project multibyte display values using the effective display order"
            retro.desktop.viewModels -> retro.desktop.ideState "Read the current bytes, rendered values and applicable scope"
            retro.desktop.debugger -> retro.desktop.viewModels "Render the memory map, raw bytes and effective byte order"
            autoLayout lr
        }
        dynamic retro.desktop "CommandLocalization" "Changing the display language refreshes command labels and icons while preserving the registered gesture and action" {
            developer -> retro.desktop.shell "Choose French (Canada) as the IDE display language"
            retro.desktop.shell -> retro.desktop.commands "Invoke the registered display-language command"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the language-change request on the registered preferences view"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the requested locale and originating registration"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed display-language change"
            retro.desktop.unitOfWork -> retro.workspaceFiles "Persist the selected language"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the selected presentation culture"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the language-change outcome for the preferences view"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver the outcome to the originating preferences view"
            retro.desktop.localizer -> retro.desktop.ideState "Observe the change to fr-CA"
            retro.desktop.localizer -> retro.desktop.shell "Notify open views to refresh localized resources"
            retro.desktop.shell -> retro.desktop.commands "Read the same label slug, icon slug, and default gesture"
            retro.desktop.shell -> retro.desktop.localizer "Resolve the label and icon by their typed slugs"
            retro.desktop.localizer -> retro.desktop.localeCatalog "Look up each resource in fr-CA, fr, then en"
            autoLayout lr
        }
        dynamic retro.desktop "BuildLaunchDebug" "Build and debug workflow using the UAE preset as one example" {
            developer -> retro.desktop.shell "Choose Start Debugging for the selected project and target"
            retro.desktop.shell -> retro.desktop.commands "Execute the shared Start Debugging AsyncRelayCommand"
            retro.desktop.commands -> retro.desktop.viewModels "Raise Start Debugging through the registered view's CommandRequested event"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the Start Debugging request with its origin and context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Start Debugging request"
            retro.desktop.unitOfWork -> retro.desktop.launch "Run the registered build-and-launch handler"
            retro.desktop.launch -> buildTools "Build and read diagnostics. Stop here on failure."
            retro.desktop.launch -> retro.desktop.sessions "On success, launch or attach using resolved artifacts"
            retro.desktop.sessions -> retro.desktop.catalog "Get the selected adapter and start the session"
            retro.desktop.catalog -> retro.desktop.uaeAdapter "Dispatch the UAE40 operation"
            retro.desktop.uaeAdapter -> uae "Request start through the planned UAE bridge"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the launch result for the originating view"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver the launch result to the originating view"
            developer -> retro.desktop.emulation "Open and view the Emulation tab"
            retro.desktop.emulation -> retro.desktop.viewModels "Request active-session media through the registered view"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the active-session media query"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the media query"
            retro.desktop.unitOfWork -> retro.desktop.sessions "Read available media for the selected session"
            retro.desktop.debugger -> retro.desktop.viewModels "On a breakpoint, request stopped state and bind supported step actions"
            retro.desktop.viewModels -> retro.desktop.controller "Submit debugger queries or raise command requests from the registered debugger view"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the debugger query or command"
            autoLayout lr
        }
        deployment * windows "WindowsDeployment" "Windows workbench, native tools, and planned local emulator targets" {
            include *
            autoLayout lr
        }
        deployment * linux "LinuxDeployment" "Linux workbench, native tools, and planned local emulator targets" {
            include *
            autoLayout lr
        }

        styles {
            element "Element" {
                shape RoundedBox
                background #275c60
                color #ffffff
            }
            element "Person" {
                shape Person
                background #265462
            }
            element "External" {
                background #57636b
                color #ffffff
            }
            element "Phase 01" {
                stroke #4eb9aa
                strokeWidth 3
            }
            element "Phase 02" {
                background #17665b
                color #ffffff
            }
            element "Phase 03" {
                background #235c49
                stroke #62aa88
                strokeWidth 3
                color #ffffff
            }
            element "Phase 04" {
                background #24557b
                stroke #74a9d0
                strokeWidth 3
                color #ffffff
            }
            element "Test Fixture" {
                background #50446e
                color #ffffff
            }
            element "Implemented" {
                background #235c49
                color #ffffff
            }
            element "Planned" {
                stroke #d9a669
                strokeWidth 3
            }
            element "DataStore" {
                shape Cylinder
            }
            relationship "Relationship" {
                color #64747c
            }
        }
    }

    configuration {
        scope softwaresystem
    }
}
