workspace "Retro IDE" "C4 baseline for the shared emulator IDE after its design moved from UAE Developer." {
    !identifiers hierarchical

    model {
        developer = person "Retro Developer" "Edits, builds, runs, tests, and debugs software for a selected retrocomputing target."

        retro = softwareSystem "Retro IDE" "Shared cross-platform development environment and capability-driven emulator integrations." {
            properties {
                "Baseline" "design-07-source-editor"
                "Implementation status" "Wireframes and target presets exist. Desktop and runtime adapter components are planned."
                "QBrain workspace" "/home/sharpninja/github/retro-ide"
            }

            desktop = container "Retro IDE Desktop" "Owns the workbench, user workflows, selected-session state, and emulator adapter components." ".NET, Avalonia, Dock.Avalonia" "Planned" {
                shell = component "Workbench and Docking (View)" "Hosts menus, command bars, keyboard bindings, documents, and docked tools. Presents command metadata and localization, and applies the system wait cursor to logically blocked UI." "Avalonia and Dock.Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-001, TR-RETRO-DESKTOP-001, FR-RETRO-007, FR-RETRO-008, TR-RETRO-COMMAND-007#ac-system-wait-cursor"
                        "Storyboard" "STB01 Arrange tool windows and restore an IDE workspace"
                    }
                }
                commands = component "Predefined Command Catalog" "Pairs a shared RelayCommand or AsyncRelayCommand with a stable ID, label slug, icon slug, and default keyboard gesture. Exposes availability and execution state and raises requests on registered views." "CommunityToolkit.Mvvm.Input (ICommand)" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-007, TR-RETRO-COMMAND-007, FR-RETRO-008, TR-RETRO-LOCALIZATION-008"
                    }
                }
                localizer = component "Localization Service" "Resolves typed text and image slugs through exact locale, parent locales, and bundled English. Formats messages and refreshes workbench presentation when the UI culture changes." ".NET localization" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-008, TR-RETRO-LOCALIZATION-008"
                    }
                }
                localeCatalog = component "Locale Catalogs" "Supplies versioned catalogs of strings, message patterns, and local SVG or PNG resources. Includes the bundled default catalog and validated language packs." "BCP 47 locale tags" "DataStore,Planned"
                controller = component "Main MVC Controller" "Owns dispatch and result delivery for views registered directly with it. Provides the shared execution entry point for typed requests from all controllers into the custom Unit of Work." "GPS.SimpleMVC 2.0.0" "Planned"
                documentController = component "Document Controller" "Owns dispatch for editor views that register directly with it. Receives their events, dispatches typed document requests through the shared CQRS entry point, and delivers results to the originating tab." "GPS.SimpleMVC 2.0.0" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-controller-ownership, TR-RETRO-COMMAND-007#ac-mdi-dispatch"
                        "Storyboard" "STB14 Edit, build, boot, and debug a UAE project"
                    }
                }
                unitOfWork = component "CQRS Unit of Work" "Owns IDE application logic. Dispatches typed commands and read-only queries to registered handlers with scoped context, validates eligibility, and returns or publishes their outcomes." "Custom .NET command and query handlers" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-cqrs-unit-of-work"
                    }
                }
                viewModels = component "IDE ViewModels (View)" "Implement IView with a stable ViewKey. Raise CommandRequested, receive correlated results on the UI thread, and expose observable properties through ObservableObject." "CommunityToolkit.Mvvm.ComponentModel and SimpleMVC" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-simplemvc-views, TR-RETRO-COMMAND-007#ac-toolkit-viewmodels"
                    }
                }
                documentViews = component "Editor Tab ViewModels (View)" "One observable IView per editor tab. Each registers directly with the document controller and raises events for the document captured at invocation." "CommunityToolkit.Mvvm.ComponentModel and SimpleMVC" "Planned"
                sourceEditor = component "Source Editor" "Renders the per-tab buffer, caret, selection, undo history, highlighting, diagnostics links, and breakpoint markers. Keeps document and debugger commands on the registered view's dispatch path." "AvaloniaEdit.TextEditor" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-011, TR-RETRO-EDITOR-011"
                        "Storyboard" "STB14 Edit, build, boot, and debug a UAE project"
                    }
                }
                uiDispatcher = component "Avalonia UI Dispatcher" "Schedules callbacks on the UI thread. The dispatch owner checks its originating registration inside the callback before applying the result." "Avalonia Dispatcher.UIThread" "Planned"
                ideState = component "IDE State (Model)" "Provides observable workspace, presentation culture, active document and target, selected session, availability, operation state, interaction blocks by scope, and diagnostics." ".NET" "Planned"
                launch = component "Project and Launch Workflow" "Selects the target preset, runs the configured build, opens source diagnostics, and launches or attaches after a successful build." ".NET" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-002, TR-RETRO-PROFILE-002"
                        "Storyboards" "STB14 Edit, build, boot, and debug a UAE project; STB16 Select an emulator target"
                    }
                }
                sessions = component "Session Coordinator" "Opens a versioned adapter session, owns its lifetime and bundle lease, routes typed capability requests, and projects state, control events, and bounded media streams." ".NET" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-010, TR-RETRO-ADAPTER-010"
                    }
                }
                emulation = component "Emulation and Replay View" "Presents video, input capture, host volume, and available frame capture. A seekable recording drives the replay playhead; live emulation shows elapsed time." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-003, TR-RETRO-AVINPUT-003"
                        "Storyboard" "STB15 Seek and adjust volume in the Emulation player"
                    }
                }
                debugger = component "Debugger and Guest Inspectors" "Displays target-aware registers, address spaces, disassembly, source, breakpoints, console, logs, and optional guest tools. Keeps raw views available when symbols or guest features are unavailable." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-004, TR-RETRO-DEBUG-004"
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
                emulators = component "Emulators Management View" "Browses buckets, compatible releases, and installed versions. Presents refresh, install, update, rollback, and remove actions and their outcomes." "Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-009, TR-RETRO-BUNDLE-009"
                    }
                }
                bundleManager = component "Emulator Bundle Manager" "Reads Git bucket manifests at a recorded commit, selects a compatible host target, verifies release archives, and installs immutable bundles with receipts." ".NET, Git, JSON, SHA-256" "Planned"
                catalog = component "Adapter Catalog and Host Contract" "Loads the selected installed binding and its factory. Defines .NET session identity, version negotiation, capability ports, typed results, resumable events, and separate media streams." "Versioned .NET contracts" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-BUNDLE-009, TR-RETRO-ADAPTER-010"
                    }
                }
                uaeAdapter = component "UAE Adapter" "Translates UAE40 profile and session operations. Exposes UAE-specific debugger data through optional extensions." ".NET; desktop-facing transport pending" "Planned"
                viceAdapter = component "VICE Adapter" "Launches the configured x64sc executable and maps supported C64 debugger operations from the loopback binary monitor. Video, audio, and input paths need separate integration." "Process and VICE binary monitor" "Planned"
                viceSharpAdapter = component "ViceSharp Adapter" "Attaches to a configured ViceSharp host and maps supported session, monitor, media, video, input, snapshot, and diagnostics operations." "ViceSharp gRPC contracts" "Planned"
            }

            workspaceFiles = container "Local Workspace Settings" "Stores IDE layout, preferences, configured bucket sources, and project settings with target preset, pinned bundle, and user artifact references." "Local files" "DataStore,Planned"
            bundleStore = container "Local Emulator Bundle Store" "Holds verified, versioned emulator archives and expanded binaries, bindings, install receipts, and the last valid bucket metadata for offline use." "Per-user filesystem" "DataStore,Planned"
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
        qbrain = softwareSystem "QBrain Retro IDE Workspace" "Owns FR-RETRO-001 through FR-RETRO-011 and their linked technical requirements and verification records." "External"

        developer -> retro.desktop.shell "Works with projects, documents, and docked tools"
        developer -> retro.desktop.emulation "Views and operates the selected Emulation tab"
        developer -> retro.desktop.sourceEditor "Edits source in the selected document tab"
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
        retro.desktop.unitOfWork -> retro.workspaceFiles "Reads and writes workspace settings and layouts through handlers" "Local files"
        retro.desktop.unitOfWork -> retro.desktop.launch "Invokes build and debug-launch handlers"
        retro.desktop.unitOfWork -> retro.desktop.sessions "Invokes session, debugger, media, and diagnostics handlers"
        retro.desktop.unitOfWork -> retro.desktop.agentGateway "Invokes configured agent launch or attach"
        retro.desktop.unitOfWork -> retro.desktop.bundleManager "Invokes repository refresh and emulator bundle lifecycle handlers"
        retro.desktop.sessions -> retro.desktop.ideState "Publishes normalized session state and capability changes"
        retro.desktop.sessions -> retro.desktop.emulation "Supplies negotiated media streams independently of control traffic"
        retro.desktop.shell -> retro.desktop.emulation "Opens the Emulation document"
        retro.desktop.shell -> retro.desktop.debugger "Opens debugging and console tools"
        retro.desktop.shell -> retro.desktop.artifacts "Opens media, snapshot, test, and diagnostic panels"
        retro.desktop.shell -> retro.desktop.agentActivity "Opens the scoped agent activity view"
        retro.desktop.shell -> retro.desktop.emulators "Opens the Emulators manager"
        retro.desktop.emulators -> retro.desktop.commands "Binds bundle management actions to registered commands"

        retro.desktop.launch -> retro.workspaceFiles "Loads project configuration and target presets" "Local files"
        retro.desktop.launch -> buildTools "Runs the configured build and collects diagnostics and outputs" "Configured process"
        retro.desktop.launch -> retro.desktop.sessions "Requests launch or attach using resolved artifacts"
        retro.desktop.emulation -> retro.desktop.viewModels "Binds registered views for selected-session media and input requests"
        retro.desktop.debugger -> retro.desktop.viewModels "Binds registered views for debugger queries and commands"
        retro.desktop.artifacts -> retro.desktop.viewModels "Binds registered views for media, snapshot, test, and diagnostic requests"
        retro.desktop.agentActivity -> retro.desktop.viewModels "Binds registered views for scoped agent requests"
        retro.desktop.agentGateway -> aiAgent "Starts or attaches a configured scoped agent" "Configured process and MCP stdio where supported"
        aiAgent -> uae "Uses the scoped UAE MCP boundary when available" "Planned MCP"

        retro.desktop.sessions -> retro.desktop.catalog "Resolves the selected adapter and invokes normalized operations"
        retro.desktop.catalog -> retro.desktop.sessions "Exposes the negotiated session and optional capability ports"
        retro.desktop.bundleManager -> buckets "Fetches configured Git sources and reads versioned manifests" "Git and JSON"
        retro.desktop.bundleManager -> emulatorReleases "Downloads the archive named for the compatible host target" "HTTPS"
        retro.desktop.bundleManager -> retro.bundleStore "Installs verified files and receipts atomically and caches valid catalog metadata"
        retro.desktop.catalog -> retro.workspaceFiles "Reads the project's pinned bundle version and manifest revision"
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
            deploymentNode "Developer Workstation" "Planned Windows topology. Emulator hosts are selectable alternatives." "Windows" {
                containerInstance retro.desktop
                containerInstance retro.workspaceFiles
                containerInstance retro.bundleStore
                softwareSystemInstance uae
                softwareSystemInstance vice
                softwareSystemInstance viceSharp
            }
        }
        linux = deploymentEnvironment "Linux Development" {
            deploymentNode "Developer Workstation" "Planned Linux topology. Emulator hosts are selectable alternatives." "Linux" {
                containerInstance retro.desktop
                containerInstance retro.workspaceFiles
                containerInstance retro.bundleStore
                softwareSystemInstance uae
                softwareSystemInstance vice
                softwareSystemInstance viceSharp
            }
        }
    }

    views {
        systemContext retro "SystemContext" "Retro IDE, its developer, emulator hosts, build tools, and agent boundary" {
            include *
            include qbrain
            autoLayout lr
        }
        container retro "Containers" "Retro IDE desktop and workspace settings, with external emulator boundaries" {
            include *
            autoLayout lr
        }
        component retro.desktop "DesktopComponents" "IDE responsibilities in the desktop container" {
            include developer retro.desktop.shell retro.desktop.commands retro.desktop.localizer retro.desktop.localeCatalog retro.desktop.controller retro.desktop.documentController retro.desktop.unitOfWork retro.desktop.viewModels retro.desktop.documentViews retro.desktop.sourceEditor retro.desktop.uiDispatcher retro.desktop.ideState retro.desktop.launch retro.desktop.sessions retro.desktop.emulation retro.desktop.debugger retro.desktop.artifacts retro.desktop.agentActivity retro.desktop.agentGateway retro.desktop.emulators retro.desktop.bundleManager retro.desktop.catalog retro.workspaceFiles retro.bundleStore buildTools aiAgent buckets emulatorReleases
            autoLayout lr
        }
        component retro.desktop "AdapterComponents" "Pinned bundle and session routing for the three adapter designs" {
            include retro.desktop.launch retro.desktop.sessions retro.desktop.catalog retro.desktop.uaeAdapter retro.desktop.viceAdapter retro.desktop.viceSharpAdapter retro.workspaceFiles retro.bundleStore uae vice viceSharp
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
            retro.desktop.unitOfWork -> retro.desktop.ideState "Save the captured document and publish the outcome"
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
        dynamic retro.desktop "EmulatorBundleInstall" "Install an emulator and its bindings from a Git bucket, then resolve its pinned version for launch" {
            developer -> retro.desktop.shell "Open Emulators to install a compatible version"
            retro.desktop.shell -> retro.desktop.emulators "Show available and installed emulator versions"
            retro.desktop.emulators -> retro.desktop.commands "Invoke Install for the selected emulator bundle"
            retro.desktop.commands -> retro.desktop.viewModels "Raise the registered installation request"
            retro.desktop.viewModels -> retro.desktop.controller "Submit the bundle and version selection"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed bundle installation command"
            retro.desktop.unitOfWork -> retro.desktop.bundleManager "Install the selected bundle for this host"
            retro.desktop.bundleManager -> buckets "Read the JSON manifest at its recorded Git commit"
            retro.desktop.bundleManager -> emulatorReleases "Download the archive for the compatible runtime identifier"
            retro.desktop.bundleManager -> retro.bundleStore "Verify SHA-256 and publish the staged bundle with its receipt"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the installation outcome for Emulators"
            retro.desktop.controller -> retro.desktop.uiDispatcher "Schedule the outcome for the originating management view"
            retro.desktop.uiDispatcher -> retro.desktop.viewModels "Deliver the outcome on the UI thread"
            retro.desktop.sessions -> retro.desktop.catalog "Resolve the selected bundle when launching a project"
            retro.desktop.catalog -> retro.workspaceFiles "Read the project version and manifest revision"
            retro.desktop.catalog -> retro.bundleStore "Resolve and retain the pinned installed entry points"
            autoLayout lr
        }
        dynamic retro.desktop "EmulatorSessionContract" "Negotiate an emulator session and use independent control and media paths, with ViceSharp as an example" {
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
        deployment * windows "WindowsDeployment" "Planned Windows workstation with selectable local emulator targets" {
            include *
            autoLayout lr
        }
        deployment * linux "LinuxDeployment" "Planned Linux workstation with selectable local emulator targets" {
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
