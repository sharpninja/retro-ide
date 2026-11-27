# Emulator adapter contract

Retro IDE uses one versioned .NET contract for emulator sessions. An adapter shipped in an emulator bundle translates that contract to its backend protocol. The workbench uses the same interface for UAE, VICE, ViceSharp, and future emulator families. The bundle and installed version determine what the IDE loads. A live handshake determines what the session can do. [Bundle architecture](architecture.md#emulator-bundles-and-buckets)

## Responsibilities

| Participant | Responsibility |
| --- | --- |
| Retro IDE | Owns projects, target selection, localization, docked tools, source navigation, and command eligibility. The registered view's controller sends typed requests through CQRS to the Session Coordinator. |
| Session Coordinator | Resolves the pinned bundle, opens a session, tracks its identity and current state, and holds the installed version for the session lifetime. It projects events to the model and gives media consumers bounded streams. |
| Bundle adapter | Implements the shared contract and translates to the selected backend. Normalizes identities, capabilities, events, limits, and errors. Backend handles, IPC details, and protocol-specific address formats stay inside the adapter. |
| Emulator host | Owns guest execution and underlying resources. Enforces its operation authorization and decides whether an operation can run in the current guest state. |

## Core .NET surface

The core surface is required for every adapter. The `RetroIde.Emulation` assembly defines the factory, session, and shared data types. It depends only on the .NET runtime. The outer contract and optional ports use typed requests and results.

```csharp
public interface IEmulatorAdapterFactory
{
    ValueTask<AdapterResult<AdapterDescriptor>> DescribeAsync(CancellationToken cancellationToken);
    ValueTask<AdapterResult<IEmulatorSession>> OpenAsync(
        SessionRequest request, CancellationToken cancellationToken);
}

public interface IEmulatorSession : IAsyncDisposable
{
    SessionIdentity Identity { get; }
    ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<SessionEvent> ReadEventsAsync(
        EventCursor? after, CancellationToken cancellationToken);
    bool TryGetPort<TPort>(CapabilityId capability, out TPort? port)
        where TPort : class, IEmulatorPort;
    ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(
        SessionCloseRequest request, CancellationToken cancellationToken);
}

public interface IEmulatorPort
{
}
```

- `AdapterDescriptor` reports the adapter ID, supported contract versions, launch or attach modes, and known target families. `AdapterContract` checks adapter identity against the manifest, checks the requested mode, and chooses the newest exact API version supported by the IDE, bundle, and adapter.
- `SessionRequest` identifies the validated target profile, requested mode, and compatible contract version. Launch uses installed bundle entry points and validated project inputs. Attach uses a configured endpoint reference. Runtime credentials stay with the connection configuration.
- `SessionIdentity` identifies the adapter, bundle version, and unique session instance. Any backend instance ID is mapped to that identity. `SessionState` contains the lifecycle and execution status, current state revision, machine and device descriptors, capability snapshot, and an event cursor for that state.
- `SessionCloseRequest` releases the IDE's connection and subscriptions. A launched session follows its declared cleanup policy. An attached host continues independently when the IDE detaches. Guest reset and power operations use the execution port and applicable host authorization.

The factory returns a usable session only after startup or attach and capability negotiation. The coordinator resolves the project pin, holds its installed version, and checks the initial identity, state, cursor, and close policy before returning the connection. Failed opens release the connection and lease. Session disposal releases streams and the bundle lease. Reconnection confirms backend instance identity and refreshes state and capabilities. A restarted or replaced backend instance gets a new session identity so a request from the previous instance cannot target it.

## Capability ports

A capability descriptor identifies a namespaced feature and version, its subject where applicable, supported operations, and limits. Subjects include CPU cores, memory spaces, drives, displays, and input devices. An adapter can obtain capabilities directly from a host or derive them from its verified protocol version and supported operations. Capabilities can change during a session. The IDE updates command availability when the model observes a new capability snapshot. The adapter validates each call against the current session and the host remains authoritative.

| Optional port | Requests and data |
| --- | --- |
| Execution and timing | Run, pause, step, reset, power off, query state and timing, and configure an advertised speed or throttle mode. Instruction, cycle, and phase stepping, along with live bus observations, have separate capability declarations. |
| CPU and memory inspection | Enumerate CPUs, registers, address spaces and banks. Read or write registers and bounded address ranges. Addresses identify a space, optional bank, and offset. Machine metadata includes width, byte order, and relevant alignment rules. Reads distinguish supported side-effect-free inspection from reads that can affect guest devices. |
| Debugger and trace | List, set, enable, and remove breakpoints and watchpoints. Observe stopped events, continue from a stop, and read disassembly or instruction traces when supported. IDE-owned symbols and source mapping can use raw inspection data. |
| Video | Enumerate display outputs, capture a frame, or subscribe to a live stream. Declare dimensions, pixel format, stride, presentation timing, and format changes. Frame capture and live streaming are distinct capabilities. |
| Audio | Enumerate sources and subscribe to PCM or an advertised encoded stream with format and timestamp metadata. The IDE's volume control adjusts host playback. Guest audio controls require a separate advertised capability. |
| Input | Enumerate devices and send ordered keyboard, pointer, or controller events. Input uses the scoped session and device, with sequencing and release of held input when capture ends. |
| Media and guest storage | Enumerate device slots and compatible formats. Mount, eject, or query media using a typed artifact reference. Local files and remote uploads use declared transfer capabilities. Guest paths are separate from host paths. |
| Snapshots and replay | Save, list, and restore snapshots with compatibility metadata. Record or open a replay, query its time base and range, and seek when the recording is seekable. Live rewind requires its own capability. |
| Console and diagnostics | Attach to guest serial or console streams, read structured logs, fetch diagnostic artifacts, and send input where supported. Keep emulator diagnostics separate from guest console traffic. |
| Automation and guest services | Expose test fixtures, assertions or instrumentation, and optional guest integrations such as service or container inspection through their own capability ports. The IDE can compose supported operations for a workflow. |

For example, a memory port exposes a bounded read and an explicit mutation. A backend that supports inspection and denies writes can advertise only the read operation.

```csharp
public interface IMemoryPort : IEmulatorPort
{
    ValueTask<AdapterResult<MemoryBlock>> ReadAsync(
        MemoryReadRequest request, CancellationToken cancellationToken);
    ValueTask<AdapterResult<MutationReceipt>> WriteAsync(
        MutationRequest<MemoryWriteRequest> request, CancellationToken cancellationToken);
}
```

## Debugger machine description

The installed package may name versioned, declarative debugger presentation profiles in its `debuggerProfiles` manifest entries. Install validates the profile paths and schema inside the hash-verified archive. A project pin retains the same binding and profile data for the lifetime of the session.

At launch, attach, or reconnect the adapter supplies the active machine description through the negotiated session contract. The descriptor identifies the instance, CPU or CPUs, exact CPU model and variant, clock domains, default byte order, display profile, and the address width and units of each memory space. Regions carry bounds, access policy, and applicable bank, segment, mirror, or overlay identity. Map updates carry a revision. The session reloads the descriptor and current map after an event gap. Bounded memory operations use the selected instance, CPU, space, mapping, and revision.

Retro IDE resolves versioned register, flag, opcode, addressing, and timing metadata for the 6502, 6809, Z80, 8080, 8086, 6800, and 68000 families. Disassembly uses the active CPU model and platform address format. Timing views label reference values separately from observed cycles, phases, and bus events. The debugger shows capability availability at the selected CPU.

The memory view displays raw bytes and typed values in separate columns. **Emulator default** uses the active address-space order or the machine or CPU default when supplied. **Little-endian** and **Big-endian** persist as user display choices. The selected order applies to typed values. Instruction decoding follows the selected CPU configuration. If the descriptor or platform profile is unavailable, the workbench identifies the limitation and exposes advertised bounded raw inspection.

The [package-backed debugger storyboard](design/_manifest.md#stb17-package-debugger) shows the package pin, the live description, target changes, bank selection, clock capability availability, and all three byte-order settings. QBrain tracks the CPU and view contract under `FR-RETRO-016`, `TR-RETRO-CPU-016`, and `TEST-RETRO-016`, and the descriptor contract under `FR-RETRO-017`, `TR-RETRO-MACHINE-017`, and `TEST-RETRO-017`.

## Commands, events, and streams

- **Commands and queries.** Queries return data for the current state or a revision identified in the result. A state-changing request names the session and operation ID and carries an expected revision when the operation uses state preconditions. An adapter serializes conflicting mutations and maps host revision or lease semantics where available. Ordered input uses its own sequence. A transport response, an applied command, and an observed guest state change are distinct.
- **Results.** Return typed data or a structured error with a stable code, the correlated operation ID, and relevant current state. Define codes for unsupported capability, unavailable operation, invalid arguments or state, incompatibility, permission denial, revision conflict, disconnection, timeout, cancellation, and operation failure. A timeout or disconnect can leave a mutation's outcome unknown. Reconcile against host state or an operation receipt before retrying a mutation whose effects may already have occurred.
- **Control events.** Carry session identity, event sequence, and state revision. Include lifecycle and capability changes, operation completion, stops, media changes, and diagnostics. The adapter can resume after a supported cursor. It explicitly reports a gap when resumption is unavailable or records have expired, so the coordinator can reload authoritative state and subscribe from the new cursor.
- **Media streams.** Carry stream ID, per-stream sequence, format, and timestamps in a declared clock domain. Frame consumers can skip stale frames. Audio and other ordered streams signal discontinuities. Media backpressure stays isolated from commands and control events. Disposing a consumer stops its subscription and releases its buffers.
- **Authorization and client routing.** Desktop UI, automation, and any MCP tools use the same session contract and capability checks. The view's controller still delivers results through Avalonia. An emulator or worker retains authority over its operations, credentials, and guest resources.

The execution port advertises each action by its stable operation ID, optionally per CPU, and may set a `max-count` step limit. The coordinator checks the request's session, actor, current revision, and capability before forwarding it. A lost execution reply leaves that mutation uncertain. The original actor can use the receipt port to ask whether it applied. A confirmed application supplies a receipt without reissuing the command. A confirmed non-application means the host will never execute the original request later. Hosts deduplicate operation IDs within the same backend instance, including after reconnect.

The first control subscription starts at the initial state's cursor. When the adapter reports an expired cursor or returns a sequence jump, the coordinator reports the gap, loads a snapshot that covers it, and subscribes after the snapshot's cursor. An older client can advance past unfamiliar event types using their common cursor and revision.

## Compatibility and backend mapping

The bundle declares its supported adapter contract versions. The IDE loads the adapter against that contract and negotiates a common version before opening the backend session. Major changes require an explicit compatibility decision. Minor changes can add optional ports, fields, and events. Machine-specific functionality uses a namespaced, versioned extension port with a declared request and result schema. Unknown optional capabilities can be ignored by the generic IDE.

| Backend | Initial translation and evidence |
| --- | --- |
| UAE Developer | Map the UAE control service's instance lifecycle, revisions, leases, and recorded events to session state and control. Map hardware inspection, media, and streaming features when the desktop bridge supplies them. The control and worker code exist. The desktop adapter is planned. [UAE Developer](https://github.com/sharpninja/uae-developer) |
| VICE | Launch the bundled `x64sc` and use the binary monitor for advertised features such as memory, registers, checkpoints, and monitor stop events. The monitor's display request supports a capture path. Continuous video, audio, and guest input need their own verified integration. [VICE binary monitor](https://vice-emu.sourceforge.io/vice_13.html) |
| ViceSharp | Translate the published gRPC session, media, video, input, monitor, snapshot, and diagnostics APIs. The existing attach preset uses its configured endpoint and bundled client binding. Discover actual capabilities after connecting. [ViceSharp host protocol](https://github.com/sharpninja/vice-sharp/blob/main/src/ViceSharp.Protocol/Protos/emulator_host.proto) |

Verify the shared contract against an in-memory fixture and then each distributed backend on its supported hosts. Cover launch and attach, capability changes, command conflicts, event gaps, stream backpressure, disconnects, and at least one current use case for each advertised port. Add a fixture for a new capability and confirm that an older client still handles the base session. QBrain tracks this contract under `FR-RETRO-010`, `TR-RETRO-ADAPTER-010`, and `TEST-RETRO-010`.
