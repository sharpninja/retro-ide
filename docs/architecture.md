# Retro IDE architecture

The [Structurizr C4 model](architecture/structurizr/README.md) locates these responsibilities in the IDE and shows the UAE, VICE, and ViceSharp integration boundaries.

Retro IDE owns the .NET desktop shell, saved layouts, project and launch configuration, source and debugger tabs, Emulation, input capture and host volume, media and snapshot dialogs, diagnostics and test views, and scoped agent activity. QBrain workspace `/home/sharpninja/github/retro-ide` owns these UI requirements.

Each session is identified by an adapter and an instance ID. The IDE consumes negotiated capabilities, machine metadata, and normalized events. The selected target drives register labels, address width, device names, media formats, and tool availability. Session-specific state follows the selected instance. Layout files remain independent of emulator state.

The IDE calls adapters through a versioned host contract. Adapters translate profile data, launch or attach to the host, and translate lifecycle, media, video, audio, input, diagnostics, and debug operations. Unsupported operations are surfaced as unavailable. Recorded timeline seeking requires a seekable recording source and is independent of live CPU stepping or rewind.

The [emulator adapter contract](emulator-adapter-contract.md) defines the .NET factory and session interfaces, optional capability ports, request and event semantics, and backend mappings.

Retro IDE exposes every supported feature as an MCP tool using the official .NET MCP hosting extensions. ViewModels define the public context contracts for those tools. CLI and desktop clients attach to the same IDE session and dispatch typed operations through the shared controller and CQRS handlers. [MCP tool contract](mcp-tools.md)

## Phase 01 implementation

The [accepted Phase 01 C4 model](architecture/structurizr/README.md) records the running Avalonia workbench, shared focus command and both MCP transports. The desktop persists dock layout independently of emulator state. UI acceptance uses SharpNinja.Avalonia.RemoteControl on real windows, with isolated Linux coverage for X11 drag and system-cursor sampling. The [implementation plan](plans/PLAN-RETROIDE-001.md#local-verification-for-every-phase) applies local input-free UI verification to each phase.

Phase 02 adds Project Explorer, editor tabs, and localization. Phase 03 adds native Git, the repository terminal, framed language-server sessions, and scoped operation handles. The Git write lease verifies the selected checkout after waiting before allowing a native write. The emulator bundle manager and real adapter lifecycle are later phases. [Phase 03 acceptance](receipts/phase-03-git-terminal-language-servers.md)

## MVVM Toolkit, SimpleMVC, and CQRS

Avalonia menus, command bars, and keyboard bindings use the predefined command catalog. Each registration provides a stable ID and one shared `ICommand` instance per activation scope. A preset supplies target data to the command. [Avalonia commanding](https://docs.avaloniaui.net/docs/input-interaction/commanding)

Use the .NET Community Toolkit's `CommunityToolkit.Mvvm` package for ViewModels and presentation commands. ViewModels derive from `ObservableObject` and use `[ObservableProperty]` for UI-bound properties. Every ViewModel implements Retro IDE's `IView`, which extends `GPS.SimpleMVC.Views.ISimpleView`, and owns a unique `Guid ViewKey` for its registration lifetime. GPS.SimpleMVC 2.0.0 provides the main MVC controller and view registry. [MVVM Toolkit package](https://www.nuget.org/packages/CommunityToolkit.Mvvm) · [GPS.SimpleMVC package](https://www.nuget.org/packages/GPS.SimpleMVC/2.0.0)

Use Toolkit `RelayCommand` for synchronous dispatch and `AsyncRelayCommand` for task-based dispatch such as Build Project and Start Debugging. Use `[RelayCommand]` on ViewModel dispatch methods, or construct those same Toolkit command types when registering a command directly in the catalog. Cache the instance in the registration so every bound control observes the same eligibility and execution state. Commands capture the originating view and invocation context and raise that view's command event. Async commands await the request's completion, expose `IsRunning`, disable repeat activation by default while pending, and forward a cancellation token for operations that support cancellation. [Toolkit command generator](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand)

- **View** binds Avalonia controls to Toolkit commands and registered ViewModels. The ViewModels project model updates for selection, progress, diagnostics, and outcomes onto the UI thread.
- **Model** exposes workspace, active document and target, selected instance, negotiated capabilities, and operation state. `CanExecute` reads published eligibility. Use `[NotifyCanExecuteChangedFor]` on dependent ViewModel properties or `IRelayCommand.NotifyCanExecuteChanged()` when eligibility changes arrive from the model or active scope.
- **Controllers** derive from `SimpleControllerBase`. A view registers directly with its dispatching controller. That controller handles the view's events, dispatches its typed requests, and schedules its results through Avalonia. The main controller owns workbench-wide views and provides the shared execution entry point to the CQRS Unit of Work.
- **CQRS Unit of Work** owns IDE application logic. A request gets a scope for its context, handlers, and dependencies. Command handlers validate the captured context, coordinate workflow and adapter effects, and publish outcomes to the model. Query handlers return read-only results. The adapter host enforces emulator authorization.

Build Project runs the configured build and publishes diagnostics. Start Debugging builds first and launches only after success. Disabled commands produce no operation. The Unit of Work revalidates eligibility after dispatch and rejects stale requests before applying changes. An emulator operation reports its own outcome through the adapter.

### View events and result delivery

1. Each `IView` registers with one controller by `ViewKey`. Registration establishes the dispatch owner. That controller subscribes once to `CommandRequested` and unsubscribes when the view closes or its registration is replaced. Menu, command-bar, and keyboard bindings use the command registered for the originating view's activation scope.
2. The Toolkit command raises `CommandRequested` on that view. The event carries the command ID, a unique request ID, the originating `ViewKey`, captured context, and a cancellation token when applicable. The owner checks the sender against its live registration, resolves the command ID, and dispatches the typed request through the main controller's shared CQRS execution entry point. The owner supplies the completion task to an async command. An invocation without a registered subscriber completes as unavailable.
3. The owner awaits the handler and uses `Dispatcher.UIThread.InvokeAsync` to deliver success, failure, or cancellation to the originating view. The callback checks that the originating registration is still live under that owner and correlates the result by request ID. Closing or replacing the registration suppresses delivery. Focus and docking changes preserve the recipient while the registration is live. Shared application state and diagnostics flow through the model. [Avalonia threading model](https://docs.avaloniaui.net/docs/app-development/threading)

### Runtime exception handling

Desktop command failures return an `internal_error` with the operation ID. The controller logs the original exception through `ILogger` and delivers the failure to the calling view. The caller can inspect current state and retry explicitly. A failed editor save retains the draft, and watcher notification failures retry delivery. An invalid saved layout or workspace uses the default layout or an empty workspace. Failure to start the optional MCP listener leaves the desktop available.

The desktop records warnings and exceptions in `logs/desktop-*.log` under its application data directory. It also writes diagnostics to standard error. MCP HTTP and stdio log request and transport failures to standard error and return generic protocol errors. Stdio reserves standard output for MCP messages. Unknown dispatcher failures and fatal startup failures are logged before controlled shutdown where the runtime permits it. Process-level handlers also log unhandled managed exceptions and unobserved task failures. Operating-system termination and native process corruption can interrupt logging and cleanup. [Avalonia dispatcher exceptions](https://api-docs.avaloniaui.net/docs/E_Avalonia_Threading_Dispatcher_UnhandledException)

### Tabbed document editing

The workbench can use a `DocumentController` for tabbed MDI. Each editor tab has its own `IView` ViewModel and registers directly with that controller. The document controller owns editor subscriptions and dispatches document commands such as Edit, Save, and Close. CQRS handlers own document operations and publish document state to the model.

The active editor tab supplies the document-scoped command bindings for its workbench window. Invoking Save captures that tab's document ID and originating registration. The document controller receives the event, dispatches the typed save request, and uses Avalonia to return its outcome to that editor. Selecting another tab during the save changes the bindings for the next command. The pending save keeps its captured document and recipient.

### Project Explorer

Project Explorer is a dockable workbench pane that displays the current workspace's projects and folders in one tree. Open Project loads a project with its metadata. Open Folder opens a directory for browsing and editing. A workspace can contain multiple roots of either kind. A project root can supply build and launch settings, and a folder root can be used for editing as soon as it opens.

- Expand and collapse folders, open a file by double-clicking or pressing Enter, and select an existing editor tab when the file is already open. Reveal in Project Explorer locates an open document in the tree. Each item carries its workspace root and path so similarly named files open in the correct tab.
- Load directory children on demand and apply the result to the workspace and root that requested it. Refresh changed branches when files are added, renamed, or removed outside the IDE. A root that is missing or inaccessible shows its status in the tree, and other roots remain browsable. Persist the workspace roots and the pane's expansion and selection state with the workspace.
- The Explorer ViewModel registers with the main controller. Its commands and queries for opening roots, browsing files, and opening documents use the shared CQRS flow. Document opening resolves the selected file and opens or activates its AvaloniaEdit tab. Document commands continue through the tab's registered `DocumentController`.

The existing workbench wireframes show the Project Explorer pane. STB01 shows its empty-workspace entry and STB14 shows it alongside the source editor. QBrain tracks the feature under `FR-RETRO-012`, `TR-RETRO-EXPLORER-012`, and `TEST-RETRO-012`.

### Git and Source Control

A Git menu and dockable Source Control pane expose repository actions as registered Toolkit commands. Project Explorer and source tabs use repository status for decorations and navigation. The Source Control ViewModel registers with the main controller. CQRS handlers invoke native Git in the captured repository and worktree, publish refreshed state, and return outcomes through Avalonia. The integrated terminal gives advanced Git workflows the same project context. [Git workflows and execution contract](git-integration.md)

### Source editor

Use `AvaloniaEdit.TextEditor` from the `Avalonia.AvaloniaEdit` NuGet package for source documents in tabbed MDI. WebScene demonstrates Monaco inside an Avalonia window, with published Monaco-specific setup and verification for macOS. Cross-platform clipboard and text composition remain open in its published compatibility scope. Avalonia also supplies a general-purpose web host for Windows and Linux. The baseline editor uses the documented AvaloniaEdit control. [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) · [WebScene Monaco compatibility](https://github.com/SceneTech/WebScene/blob/main/docs/monaco-compatibilty.md#remaining-compatibility-boundary) · [Avalonia NativeWebView](https://docs.avaloniaui.net/controls/web/nativewebview)

- Each editor tab keeps its own text buffer, caret, selection, undo history, and dirty state when the tab is switched or moved to a floating workbench window. Provide line numbers, find and replace, and syntax highlighting for registered source grammars. Use the TextMate integration where a matching grammar is available and display other files as plain text. [AvaloniaEdit controls and TextMate integration](https://github.com/AvaloniaUI/AvaloniaEdit#avaloniaedit)
- The control handles immediate text input and editing gestures. The tab's `IView` and `DocumentController` retain ownership of document commands through CQRS. Save captures the document ID, buffer contents, and revision. A completed save clears the dirty state for the saved revision, and later edits remain dirty. A failed save keeps the buffer and reports the error to the originating tab.
- Build diagnostics and debugger source locations identify a project file and line, with a column when available. Navigation opens or selects the editor tab and reveals the location. An editor gutter action requests a breakpoint through the registered controller and negotiated emulator adapter. The editor displays the reported breakpoint state, while raw debugger tools remain usable when source mapping is unavailable.

QBrain tracks the editor under `FR-RETRO-011`, `TR-RETRO-EDITOR-011`, and `TEST-RETRO-011`.

### Language servers

Retro IDE hosts LSP servers for workspace source files and exposes negotiated features inline in AvaloniaEdit, through editor commands, and in Problems. The language service manages server lifecycle and versioned document synchronization. Editor requests use the registered controller and CQRS flow. Server-originated edits use typed requests through the main controller. [Language server contract](language-servers.md)

### System wait cursor

When a background operation temporarily blocks interaction, publish the affected UI scope in the model. Each affected Avalonia surface applies the platform's standard wait cursor (`StandardCursorType.Wait`) over the blocked region on the UI thread. A whole-window block covers the window's blocked client area. The view keeps processing paint and input, and available actions such as Cancel keep their normal pointer. Background operations that leave interaction available keep the normal pointer. [Avalonia cursor management](https://docs.avaloniaui.net/docs/input-interaction/pointer#cursor-management)

Track each operation's block independently and release it on completion, failure, or cancellation. Keep the wait cursor while any operation still blocks that region. When the last block clears, remove the temporary override so the surface resumes its normal cursor, including text and resize cursors. Apply the same scope rules to docked and floating views.

QBrain tracks this design under `FR-RETRO-007`, `TR-RETRO-COMMAND-007`, and `TEST-RETRO-007`. STB14 illustrates the Build and Start Debugging workflow.

## Command metadata and localization

Every command registration pairs its shared `ICommand` with an immutable descriptor containing a stable command ID, label slug, icon slug, and default keyboard gesture. A descriptor can specify a Windows or Linux gesture override where needed. The workbench derives both the displayed shortcut and the keyboard binding from the effective gesture. Menus, command bars, and keyboard bindings invoke the same command within its activation scope and honor its `CanExecute` state. Guest input capture follows its own focus and input routing policy.

| Illustrative command ID | Label slug | Icon slug | Default gesture |
| --- | --- | --- | --- |
| `project.build` | `command.project.build.label` | `command.project.build.icon` | Ctrl+Shift+B |
| `debug.start` | `command.debug.start.label` | `command.debug.start.icon` | F5 |

A shared localization service resolves a namespaced slug as a text or image resource from a versioned locale catalog. The default English (`en`) catalog ships with the IDE. Additional language packs declare a BCP 47 locale and supply translated strings, message patterns, and optional image variants. Each pack has a manifest with typed entries. A text entry holds a UTF-8 string or message pattern. An image entry names a packaged SVG or PNG relative to that pack. Translators can reuse the default icon by leaving the translated image entry absent. Explorer root captions and availability messages use the same catalog and keep the filesystem names as typed arguments. The service uses the same lookup for command labels, tooltips, accessible names, diagnostics, and other IDE-owned UI resources. [BCP 47 language tags](https://www.rfc-editor.org/info/rfc5646/)

- **Lookup.** Resolve each slug independently from the selected locale to its parent tags and then to `en`. For `fr-CA`, the service checks `fr-CA`, `fr`, and `en` in order. A missing or unusable entry advances the lookup and produces a diagnostic for malformed entries. If the default also fails, return a visible placeholder string or fallback icon and record the missing slug and resource kind. [BCP 47 lookup](https://www.rfc-editor.org/rfc/rfc4647.html#section-3.4)
- **Formatting.** Support named arguments, plural and select forms, and locale-aware number and date formatting. Use the resolved message's locale for plural rules and the user's formatting culture for number and date values. Preserve parameters as typed data so translations can reorder them. [ICU message formatting](https://unicode-org.github.io/icu/userguide/format_parse/messages/)
- **Language changes.** Start with the OS language or a saved IDE choice. A language change goes through the main MVC controller and CQRS Unit of Work, which persists the choice and publishes the updated presentation culture. Open windows refresh text, images, accessible names, and layout direction through Avalonia bindings. The command ID, default gesture, and handler stay the same when the UI language changes.
- **Validation.** Check command IDs, required descriptor fields, default resource coverage, resource kinds, gesture syntax, and overlapping shortcut scopes at build time. Validate language pack manifests, message arguments, and that local image paths resolve within the pack before accepting a pack. Tests cover a partial locale, a right-to-left locale, and a pseudo-localized catalog with expanded strings.

#### Install a language pack

Place `manifest.json` and its images under `RetroIde/locales/<tag>/` in your user’s local application data directory. On Windows this is under `%LOCALAPPDATA%`. On Linux it is under `$XDG_DATA_HOME`, normally `~/.local/share`. Launch the IDE and choose the locale under **View → Display Language**.

```json
{
  "version": 1,
  "locale": "fr-CA",
  "text": {
    "command.workspace.open-folder.label": "Ouvrir un dossier…"
  },
  "images": {
    "command.workspace.open-folder.icon": "icons/open-folder.svg"
  }
}
```

Leave a slug out to inherit it. To translate a named message, use an object with `pattern` and an `arguments` map. Keep the source argument names and types (`text`, `number`, or `date`). Package images as relative SVG or PNG files. SVGs can reference elements in the same image, such as a gradient by ID.

QBrain tracks command presentation and localization under `FR-RETRO-008`, `TR-RETRO-LOCALIZATION-008`, and `TEST-RETRO-008`.

## Emulator bundles and buckets

The **Emulators** manager discovers, installs, updates, rolls back, and removes emulator bundles. A bundle version ships the emulator executables, Retro IDE bindings, and their managed or native runtime files together for each supported host platform and architecture. The IDE's built-in bucket is identified as `official`. Users can add other Git-backed buckets through the manager. The catalog distinguishes available and installed versions, shows host compatibility, and lets the user select the version a project will use.

### Publishing a bundle

Use a Git repository with one JSON manifest per emulator at `bucket/<id>.json`. Publishers upload versioned archives to release hosting, then commit manifests that point to those artifacts. The manager refreshes catalog metadata from the configured Git source and records the exact commit used to read each manifest. This follows Scoop's Git bucket and JSON manifest convention with a Retro IDE bundle format. [Scoop buckets](https://github.com/ScoopInstaller/Scoop/wiki/Buckets) · [Scoop app manifests](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifests)

Each configured source has its own bare cache. Refresh reads regular manifest blobs at the remote's default commit and publishes the new catalog reference after every manifest validates. A failed refresh leaves the last valid reference readable offline. The cache retains verified commits so project pins can still resolve after the publisher rewrites a branch. A previously seen bundle version keeps its host targets, archive digests, entry points, adapter contract versions, and debugger profile definitions. Descriptive metadata and archive mirrors can change without changing the release.

| Manifest field | Contract |
| --- | --- |
| `schemaVersion`, `id`, `version` | Format `1`, stable lowercase bundle ID, and immutable SemVer release version. Publish a new version when an archive or its entry points change. |
| `name`, `description`, `homepage`, `license` | Catalog display and publisher-provided release metadata. |
| `adapterId`, `adapterApi` | Stable lowercase adapter ID and a list of compatible contract versions written as `major.minor` strings. |
| `targets` | Map from each host runtime identifier to `url`, `sha256`, and `entryPoints`. Each target includes a `binding` entry point and any emulator or worker entry points it needs. Entry-point paths are archive-relative and use forward slashes on every host. [Portable .NET runtime identifiers](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) |
| `debuggerProfiles` | Optional map from a profile ID to its `schemaVersion` and archive-relative `path`. The selected package version pins these definitions. The active adapter supplies the machine description and map revision when it opens a session. [Debugger machine description](emulator-adapter-contract.md#debugger-machine-description) |

### Installing and running a bundle

1. Select a bundle release for a supported host and adapter contract. The manager streams the artifact declared by the pinned manifest into private staging and verifies the ZIP archive's advertised SHA-256 before extraction. It can read a local artifact when its bucket is configured from a local repository. Archive paths use forward slashes and are checked for case collisions, file and directory conflicts, and special file types. The manager checks that declared entry points and debugger profile assets exist as regular files. It limits the ZIP to 2 GiB, expanded content to 8 GiB, and paths to 65,536. Catalog refresh and package install treat manifests and debugger profiles as data.
2. The store puts the exact manifest bytes, a versioned receipt, and the verified files in staging on the store filesystem. It publishes them together with one directory rename. The receipt records the bucket, Git commit, bundle ID, release version, host target, archive SHA-256, and manifest SHA-256. A repeat install of the same pin reuses the verified receipt and rejects conflicting data. Failed or cancelled installs leave existing versions usable. The manager can browse its last valid catalog offline and resolve installed bundles without contacting a bucket.
3. Projects store the active pin and prior selections in `.retroide/emulator.json`. The file records each pin's bucket, bundle ID, release version, and manifest revision so it can travel with the project. The adapter catalog resolves the pinned, compatible local receipt and uses its entry points. The manager can fetch the recorded manifest revision to install that version on another workstation where its artifact is available for the host. Missing or incompatible bundles surface in **Emulators** so the user can install or select a version. A feed refresh or update leaves the project pin in place. Rollback selects a previous pin with a compatible verified local installation. Session owners hold shared filesystem leases for their exact installed version. Removal takes an exclusive lease and moves the complete version out of the installed store before deleting its files.

Bucket operations use the existing controller and CQRS command flow. Configured sources live in `emulators/buckets.json` under the IDE application data directory and are shared by workbenches using that directory. Presets describe the guest machine and launch or attach settings, while bundle manifests describe host executables and bindings. ROMs and project media remain user-supplied. QBrain tracks the packaging and publishing contract under `FR-RETRO-009`, `TR-RETRO-BUNDLE-009`, and `TEST-RETRO-009`. [Emulator bucket tools](mcp-tools.md#emulator-bucket-tools)

## Emulator adapters

| Preset | Integration boundary | Current evidence |
| --- | --- | --- |
| UAE | The UAE bundle packages the worker and binding for the UAE control interface. `uae-developer` owns the UAE40 model, worker, protocol, and hardware-specific extensions. | The UAE control and worker projects exist. The desktop-facing bridge still needs implementation. |
| VICE | The VICE bundle packages `x64sc` and its monitor binding. The launched process binds the binary monitor to loopback. | The monitor is the documented debugging interface. Video, audio, and input integration need independent capability checks. |
| ViceSharp | The ViceSharp bundle supplies the host binaries and gRPC binding. The attach preset connects to a configured host using that binding. | The protocol defines session, media, video, input, monitor, snapshot, and diagnostics services. Transport availability and audio are negotiated. |

Adapter capabilities are discovered from the selected host after bundle resolution. Project paths, local endpoints, and capability authorization remain subject to the host policy.

Sources: [UAE Developer](https://github.com/sharpninja/uae-developer), [VICE invocation manual](https://vice-emu.sourceforge.io/vice_2.html), [VICE binary monitor manual](https://vice-emu.sourceforge.io/manual/vice.pdf), and [ViceSharp host protocol](https://github.com/sharpninja/vice-sharp/blob/main/src/ViceSharp.Protocol/Protos/emulator_host.proto).
