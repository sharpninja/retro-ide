workspace "Retro IDE" "C4 baseline for the shared emulator IDE after its design moved from UAE Developer." {
    !identifiers hierarchical

    model {
        developer = person "Retro Developer" "Edits, builds, runs, tests, and debugs software for a selected retrocomputing target."

        retro = softwareSystem "Retro IDE" "Shared cross-platform development environment and capability-driven emulator integrations." {
            properties {
                "Baseline" "design-00-ide-split"
                "Implementation status" "Wireframes and target presets exist. Desktop and runtime adapter components are planned."
                "QBrain workspace" "/home/sharpninja/github/retro-ide"
            }

            desktop = container "Retro IDE Desktop" "Owns the workbench, user workflows, selected-session state, and emulator adapter components." ".NET, Avalonia, Dock.Avalonia" "Planned" {
                shell = component "Workbench and Docking" "Hosts document and tool windows, project navigation, command UI, and saved layouts. Emulation is a document tab." "Avalonia and Dock.Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-001, TR-RETRO-DESKTOP-001"
                        "Storyboard" "STB01 Arrange tool windows and restore an IDE workspace"
                    }
                }
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
                agentActivity = component "Agent Session and Activity" "Launches or attaches configured agents and displays scoped operations, denials, transport failures, and reattachment state." ".NET and Avalonia" "Planned" {
                    properties {
                        "Requirements" "FR-RETRO-006, TR-RETRO-AGENT-006"
                    }
                }
                catalog = component "Adapter Catalog and Host Contract" "Resolves target presets to adapters and normalizes capabilities, machine metadata, instance operations, events, and optional streams." "Versioned .NET contracts" "Planned"
                uaeAdapter = component "UAE Adapter" "Translates UAE40 profile and session operations. Exposes UAE-specific debugger data through optional extensions." ".NET; desktop-facing transport pending" "Planned"
                viceAdapter = component "VICE Adapter" "Launches the configured x64sc executable and maps supported C64 debugger operations from the loopback binary monitor. Video, audio, and input paths need separate integration." "Process and VICE binary monitor" "Planned"
                viceSharpAdapter = component "ViceSharp Adapter" "Attaches to a configured ViceSharp host and maps supported session, monitor, media, video, input, snapshot, and diagnostics operations." "ViceSharp gRPC contracts" "Planned"
            }

            workspaceFiles = container "Local Workspace Settings" "Stores IDE layout and project settings independently of emulator state; includes references to target presets and user-supplied project artifacts." "Local files" "DataStore,Planned"
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
        qbrain = softwareSystem "QBrain Retro IDE Workspace" "Owns FR-RETRO-001 through FR-RETRO-006 and their linked technical requirements and verification records." "External"

        developer -> retro.desktop.shell "Works with projects, documents, and docked tools"
        developer -> retro.desktop.launch "Starts a build or debug launch"
        developer -> retro.desktop.emulation "Views and operates the selected Emulation tab"
        developer -> qbrain "Reviews IDE requirements and traceability" "Project workflow"

        retro.desktop.shell -> retro.workspaceFiles "Saves and restores IDE layouts" "Local files"
        retro.desktop.shell -> retro.desktop.launch "Routes project and debug commands"
        retro.desktop.shell -> retro.desktop.emulation "Opens the Emulation document"
        retro.desktop.shell -> retro.desktop.debugger "Opens debugging and console tools"
        retro.desktop.shell -> retro.desktop.artifacts "Opens media, snapshot, test, and diagnostic panels"
        retro.desktop.shell -> retro.desktop.agentActivity "Opens the scoped agent activity view"

        retro.desktop.launch -> retro.workspaceFiles "Loads project configuration and target presets" "Local files"
        retro.desktop.launch -> buildTools "Runs the configured build and collects diagnostics and outputs" "Configured process"
        retro.desktop.launch -> retro.desktop.sessions "Requests launch or attach using resolved artifacts"
        retro.desktop.emulation -> retro.desktop.sessions "Reads selected-session media and sends supported guest input"
        retro.desktop.debugger -> retro.desktop.sessions "Inspects normalized debugger state and issues supported controls"
        retro.desktop.artifacts -> retro.desktop.sessions "Uses supported media, snapshots, tests, and diagnostic operations"
        retro.desktop.agentActivity -> retro.desktop.sessions "Associates agent activity and permissions with the selected session"
        retro.desktop.agentActivity -> aiAgent "Starts or attaches a configured scoped agent" "Configured process and MCP stdio where supported"
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
        component retro.desktop "DesktopComponents" "Migrated IDE responsibilities in the desktop container" {
            include developer retro.desktop.shell retro.desktop.launch retro.desktop.sessions retro.desktop.emulation retro.desktop.debugger retro.desktop.artifacts retro.desktop.agentActivity retro.desktop.catalog retro.workspaceFiles buildTools aiAgent
            autoLayout lr
        }
        component retro.desktop "AdapterComponents" "Profile and session routing for the three adapter designs" {
            include retro.desktop.launch retro.desktop.sessions retro.desktop.catalog retro.desktop.uaeAdapter retro.desktop.viceAdapter retro.desktop.viceSharpAdapter retro.workspaceFiles uae vice viceSharp
            autoLayout lr
        }
        dynamic retro.desktop "BuildLaunchDebug" "Build and debug workflow using the UAE preset as one example" {
            developer -> retro.desktop.launch "Start Debugging for the selected project and target"
            retro.desktop.launch -> buildTools "Build and read diagnostics. Stop here on failure."
            retro.desktop.launch -> retro.desktop.sessions "On success, launch or attach using resolved artifacts"
            retro.desktop.sessions -> retro.desktop.catalog "Get the selected adapter and start the session"
            retro.desktop.catalog -> retro.desktop.uaeAdapter "Dispatch the UAE40 operation"
            retro.desktop.uaeAdapter -> uae "Request start through the planned UAE bridge"
            developer -> retro.desktop.emulation "Open and view the Emulation tab"
            retro.desktop.emulation -> retro.desktop.sessions "Observe the active session and available media"
            retro.desktop.debugger -> retro.desktop.sessions "On a breakpoint, inspect the stopped location and issue supported steps"
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
