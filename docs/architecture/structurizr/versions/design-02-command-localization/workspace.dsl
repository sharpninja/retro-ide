workspace "Retro IDE" "C4 baseline for the shared emulator IDE after its design moved from UAE Developer." {
    !identifiers hierarchical

    model {
        developer = person "Retro Developer" "Edits, builds, runs, tests, and debugs software for a selected retrocomputing target."

        retro = softwareSystem "Retro IDE" "Shared cross-platform development environment and capability-driven emulator integrations." {
            properties {
                "Baseline" "design-02-command-localization"
                "Implementation status" "Wireframes and target presets exist. Desktop and runtime adapter components are planned."
                "QBrain workspace" "/home/sharpninja/github/retro-ide"
            }

            desktop = container "Retro IDE Desktop" "Owns the workbench, user workflows, selected-session state, and emulator adapter components." ".NET, Avalonia, Dock.Avalonia" "Planned" {
                shell = component "Workbench and Docking (View)" "Hosts menus, command bars, keyboard bindings, documents, and docked tools. Presents command metadata from the catalog and resolves UI strings and images for the active locale." "Avalonia and Dock.Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-001, TR-RETRO-DESKTOP-001, FR-RETRO-007, FR-RETRO-008"
                        "Storyboard" "STB01 Arrange tool windows and restore an IDE workspace"
                    }
                }
                commands = component "Predefined Command Catalog" "Pairs each shared ICommand with a stable ID, label slug, icon slug, and default keyboard gesture. Reads model state for availability and delegates Execute to the controller." "System.Windows.Input.ICommand" "Planned" {
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
                controller = component "Main MVC Controller" "Derives from SimpleControllerBase, manages view registration by ViewKey, and dispatches predefined IDE actions and queries into the custom Unit of Work." "GPS.SimpleMVC 2.0.0" "Planned"
                unitOfWork = component "CQRS Unit of Work" "Owns IDE application logic. Dispatches typed commands and read-only queries to registered handlers with scoped context, validates eligibility, and returns or publishes their outcomes." "Custom .NET command and query handlers" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-cqrs-unit-of-work"
                    }
                }
                viewModels = component "IDE ViewModels (View)" "Every ViewModel implements the Retro IDE IView interface, which extends SimpleMVC ISimpleView. Each live view instance supplies a stable ViewKey and exposes model state to Avalonia bindings." "IView : GPS.SimpleMVC.Views.ISimpleView" "Planned" {
                    properties {
                        "Requirements" "TR-RETRO-COMMAND-007#ac-simplemvc-views"
                    }
                }
                ideState = component "IDE State (Model)" "Provides observable workspace, presentation culture, active document and target, selected session, availability, operation state, and diagnostics for the view." ".NET" "Planned"
                launch = component "Project and Launch Workflow" "Selects the target preset, runs the configured build, opens source diagnostics, and launches or attaches after a successful build." ".NET" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-002, TR-RETRO-PROFILE-002"
                        "Storyboards" "STB14 Edit, build, boot, and debug a UAE project; STB16 Select an emulator target"
                    }
                }
                sessions = component "Session Coordinator" "Tracks adapter and instance identity, lifecycle, operations, capabilities, and connection state for the selected session. Routes commands through its adapter." ".NET" "Planned"
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
                catalog = component "Adapter Catalog and Host Contract" "Resolves target presets to adapters and normalizes capabilities, machine metadata, instance operations, events, and optional streams." "Versioned .NET contracts" "Planned"
                uaeAdapter = component "UAE Adapter" "Translates UAE40 profile and session operations. Exposes UAE-specific debugger data through optional extensions." ".NET; desktop-facing transport pending" "Planned"
                viceAdapter = component "VICE Adapter" "Launches the configured x64sc executable and maps supported C64 debugger operations from the loopback binary monitor. Video, audio, and input paths need separate integration." "Process and VICE binary monitor" "Planned"
                viceSharpAdapter = component "ViceSharp Adapter" "Attaches to a configured ViceSharp host and maps supported session, monitor, media, video, input, snapshot, and diagnostics operations." "ViceSharp gRPC contracts" "Planned"
            }

            workspaceFiles = container "Local Workspace Settings" "Stores IDE layout, user preferences including display language, and project settings independently of emulator state. Includes references to target presets and user-supplied project artifacts." "Local files" "DataStore,Planned"
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
        qbrain = softwareSystem "QBrain Retro IDE Workspace" "Owns FR-RETRO-001 through FR-RETRO-008 and their linked technical requirements and verification records." "External"

        developer -> retro.desktop.shell "Works with projects, documents, and docked tools"
        developer -> retro.desktop.emulation "Views and operates the selected Emulation tab"
        developer -> qbrain "Reviews IDE requirements and traceability" "Project workflow"

        retro.desktop.shell -> retro.desktop.commands "Reads descriptors and binds menus, command bars, and shortcuts to the same predefined command" "ICommand"
        retro.desktop.shell -> retro.desktop.localizer "Resolves labels, accessible names, and icon slugs for rendering"
        retro.desktop.localizer -> retro.desktop.ideState "Observes the current presentation culture"
        retro.desktop.localizer -> retro.desktop.localeCatalog "Finds typed resources using the locale fallback chain"
        retro.desktop.localizer -> retro.desktop.shell "Notifies open windows to refresh localized presentation"
        retro.desktop.shell -> retro.desktop.viewModels "Binds to the registered workspace, document, and tool ViewModels" "Avalonia bindings"
        retro.desktop.viewModels -> retro.desktop.controller "Register and unregister by ViewKey and request explicit queries"
        retro.desktop.viewModels -> retro.desktop.ideState "Observe model state and expose it to the view"
        retro.desktop.commands -> retro.desktop.ideState "Reads handler-published eligibility for CanExecute and observes changes"
        retro.desktop.commands -> retro.desktop.controller "Dispatches Execute with the registered command ID and typed context" "ICommand"
        retro.desktop.controller -> retro.desktop.unitOfWork "Dispatches a typed command or query in its captured workspace context"
        retro.desktop.unitOfWork -> retro.desktop.ideState "Reads state for queries; commands publish progress, completion, and errors"
        retro.desktop.unitOfWork -> retro.workspaceFiles "Reads and writes workspace settings and layouts through handlers" "Local files"
        retro.desktop.unitOfWork -> retro.desktop.launch "Invokes build and debug-launch handlers"
        retro.desktop.unitOfWork -> retro.desktop.sessions "Invokes session, debugger, media, and diagnostics handlers"
        retro.desktop.unitOfWork -> retro.desktop.agentGateway "Invokes configured agent launch or attach"
        retro.desktop.sessions -> retro.desktop.ideState "Publishes normalized session state and capability changes"
        retro.desktop.shell -> retro.desktop.emulation "Opens the Emulation document"
        retro.desktop.shell -> retro.desktop.debugger "Opens debugging and console tools"
        retro.desktop.shell -> retro.desktop.artifacts "Opens media, snapshot, test, and diagnostic panels"
        retro.desktop.shell -> retro.desktop.agentActivity "Opens the scoped agent activity view"

        retro.desktop.launch -> retro.workspaceFiles "Loads project configuration and target presets" "Local files"
        retro.desktop.launch -> buildTools "Runs the configured build and collects diagnostics and outputs" "Configured process"
        retro.desktop.launch -> retro.desktop.sessions "Requests launch or attach using resolved artifacts"
        retro.desktop.emulation -> retro.desktop.controller "Submits selected-session media and input requests"
        retro.desktop.debugger -> retro.desktop.controller "Submits debugger queries and supported debugger commands"
        retro.desktop.artifacts -> retro.desktop.controller "Submits media, snapshot, test, and diagnostic requests"
        retro.desktop.agentActivity -> retro.desktop.controller "Submits scoped agent requests for the selected session"
        retro.desktop.agentGateway -> aiAgent "Starts or attaches a configured scoped agent" "Configured process and MCP stdio where supported"
        aiAgent -> uae "Uses the scoped UAE MCP boundary when available" "Planned MCP"

        retro.desktop.sessions -> retro.desktop.catalog "Resolves the selected adapter and invokes normalized operations"
        retro.desktop.catalog -> retro.desktop.uaeAdapter "Resolves UAE presets"
        retro.desktop.catalog -> retro.desktop.viceAdapter "Resolves VICE presets"
        retro.desktop.catalog -> retro.desktop.viceSharpAdapter "Resolves ViceSharp presets"
        retro.desktop.uaeAdapter -> uae "Launches or attaches through UAE control when a desktop-facing bridge is available" "Planned UAE control bridge"
        retro.desktop.viceAdapter -> vice "Starts x64sc and exchanges supported debugger commands and events" "Local process and loopback binary monitor"
        retro.desktop.viceSharpAdapter -> viceSharp "Attaches to a configured host and uses its published session services" "gRPC"

        windows = deploymentEnvironment "Windows Development" {
            deploymentNode "Developer Workstation" "Planned Windows topology. Emulator hosts are selectable alternatives." "Windows" {
                containerInstance retro.desktop
                containerInstance retro.workspaceFiles
                softwareSystemInstance uae
                softwareSystemInstance vice
                softwareSystemInstance viceSharp
            }
        }
        linux = deploymentEnvironment "Linux Development" {
            deploymentNode "Developer Workstation" "Planned Linux topology. Emulator hosts are selectable alternatives." "Linux" {
                containerInstance retro.desktop
                containerInstance retro.workspaceFiles
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
            include developer retro.desktop.shell retro.desktop.commands retro.desktop.localizer retro.desktop.localeCatalog retro.desktop.controller retro.desktop.unitOfWork retro.desktop.viewModels retro.desktop.ideState retro.desktop.launch retro.desktop.sessions retro.desktop.emulation retro.desktop.debugger retro.desktop.artifacts retro.desktop.agentActivity retro.desktop.agentGateway retro.desktop.catalog retro.workspaceFiles buildTools aiAgent
            autoLayout lr
        }
        component retro.desktop "AdapterComponents" "Profile and session routing for the three adapter designs" {
            include retro.desktop.launch retro.desktop.sessions retro.desktop.catalog retro.desktop.uaeAdapter retro.desktop.viceAdapter retro.desktop.viceSharpAdapter retro.workspaceFiles uae vice viceSharp
            autoLayout lr
        }
        dynamic retro.desktop "CommandDispatch" "Menu, command-bar, and keyboard actions use the same ICommand, SimpleMVC controller, and CQRS Unit of Work" {
            developer -> retro.desktop.shell "Choose Build Project from the menu, from the command bar, or with its shortcut"
            retro.desktop.shell -> retro.desktop.commands "Execute the same predefined Build Project ICommand"
            retro.desktop.commands -> retro.desktop.controller "Dispatch Build Project for the active context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Build Project request"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Recheck context and publish the pending operation"
            retro.desktop.unitOfWork -> retro.desktop.launch "Run the registered build-only handler"
            retro.desktop.launch -> buildTools "Run the configured build and collect diagnostics"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish completion or source diagnostics"
            retro.desktop.viewModels -> retro.desktop.ideState "Observe the updated build output and diagnostics"
            retro.desktop.shell -> retro.desktop.viewModels "Render the updated ViewModel"
            autoLayout lr
        }
        dynamic retro.desktop "CommandLocalization" "Changing the display language refreshes command labels and icons while preserving the registered gesture and action" {
            developer -> retro.desktop.shell "Choose French (Canada) as the IDE display language"
            retro.desktop.shell -> retro.desktop.commands "Invoke the registered display-language command"
            retro.desktop.commands -> retro.desktop.controller "Submit the requested locale"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed display-language change"
            retro.desktop.unitOfWork -> retro.workspaceFiles "Persist the selected language"
            retro.desktop.unitOfWork -> retro.desktop.ideState "Publish the selected presentation culture"
            retro.desktop.localizer -> retro.desktop.ideState "Observe the change to fr-CA"
            retro.desktop.localizer -> retro.desktop.shell "Notify open views to refresh localized resources"
            retro.desktop.shell -> retro.desktop.commands "Read the same label slug, icon slug, and default gesture"
            retro.desktop.shell -> retro.desktop.localizer "Resolve the label and icon by their typed slugs"
            retro.desktop.localizer -> retro.desktop.localeCatalog "Look up each resource in fr-CA, fr, then en"
            autoLayout lr
        }
        dynamic retro.desktop "BuildLaunchDebug" "Build and debug workflow using the UAE preset as one example" {
            developer -> retro.desktop.shell "Choose Start Debugging for the selected project and target"
            retro.desktop.shell -> retro.desktop.commands "Execute the predefined Start Debugging ICommand"
            retro.desktop.commands -> retro.desktop.controller "Dispatch Start Debugging for the active context"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the typed Start Debugging request"
            retro.desktop.unitOfWork -> retro.desktop.launch "Run the registered build-and-launch handler"
            retro.desktop.launch -> buildTools "Build and read diagnostics. Stop here on failure."
            retro.desktop.launch -> retro.desktop.sessions "On success, launch or attach using resolved artifacts"
            retro.desktop.sessions -> retro.desktop.catalog "Get the selected adapter and start the session"
            retro.desktop.catalog -> retro.desktop.uaeAdapter "Dispatch the UAE40 operation"
            retro.desktop.uaeAdapter -> uae "Request start through the planned UAE bridge"
            developer -> retro.desktop.emulation "Open and view the Emulation tab"
            retro.desktop.emulation -> retro.desktop.controller "Request active-session media"
            retro.desktop.controller -> retro.desktop.unitOfWork "Dispatch the media query"
            retro.desktop.unitOfWork -> retro.desktop.sessions "Read available media for the selected session"
            retro.desktop.debugger -> retro.desktop.controller "On a breakpoint, request stopped state and submit supported step actions"
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
