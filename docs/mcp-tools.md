# Retro IDE MCP tools

Every supported Retro IDE feature has a Model Context Protocol (MCP) tool. CLI and desktop agents can inspect state and invoke the same operations available through the workbench. Use the official MCP C# SDK and Microsoft hosting extensions. `ModelContextProtocol` provides server registration and stdio, and `ModelContextProtocol.AspNetCore` provides Streamable HTTP. Use `ModelContextProtocol.Extensions.Tasks` for long-running operations when the client negotiates it. [Microsoft .NET SDK guidance](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)

## One feature registry

Register each IDE command and query with a stable ID, typed input and output, context, and required capabilities. The workbench binds its commands to these registrations. The MCP host describes the same operations with stable tool names and JSON Schemas for `tools/list`, then maps `tools/call` requests to the registered command or query. UI gestures such as docking have semantic tool operations such as dock, float, and focus. A coverage check compares the complete feature registry to the MCP tool registry and fails when a supported operation has no tool. [MCP tool protocol](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)

MCP calls enter the main MVC controller's typed dispatch entry point and run through the CQRS Unit of Work. They carry the authenticated client and captured workspace, document, emulator instance, or workbench view identity as appropriate. Calls originating in Avalonia continue to use the `IView` that registered with its controller. An MCP call returns its result to the requesting client. Model updates refresh attached views on the Avalonia UI thread.

## ViewModels as public context contracts

The IDE ViewModels are the public source for MCP tool context. Each tool declares the ViewModel contract it uses. Its context schema is generated from the public members that the ViewModel explicitly exposes to MCP. Return those members as a read-only snapshot of the ViewModel's current state, using the same field names and meanings in discovery results and tool calls. Command arguments and results keep their typed CQRS contracts.

For example, an editor tool uses the registered editor ViewModel's workspace ID, document ID, `ViewKey`, and document revision. The agent reads the context, then sends those fields to identify the document and its expected revision when applying edits. The host resolves them against the live registration and authorized workspace before dispatch. A closed view, stale revision, or mismatched target produces a structured result. Context fields describe or select state. Commands apply changes through their existing handlers.

Mark MCP-visible members on the ViewModels and generate their schemas and serialization from that metadata. Capture view-bound state on the Avalonia UI thread. Limit the wire context to those declared members so callbacks, commands, service references, and credentials stay internal. Test schema parity between the ViewModel and tool discovery whenever a public context changes.

## Tool coverage

The names below illustrate discoverable tools. Each area includes read and mutation operations wherever the IDE supports them.

| IDE feature | MCP tool coverage |
| --- | --- |
| Workbench and localization | List command availability, inspect and change view focus and docking, manage layouts, open tool windows, and get or set language and preferences. |
| Workspaces and Project Explorer | Open projects and folders, choose workspaces and roots, list children, open files, and reveal documents. |
| Source editor and language servers | Inspect open buffers and revisions, open, edit, save and close documents, manage language server settings and lifecycle, and request completions, symbols, navigation, diagnostics, formatting and refactors. |
| Git and terminal | Inspect repositories, worktrees, status, diffs and history. Expose the same staging, branch, commit, remote, conflict and other Git operations. Open, read, write, and close a terminal for the authorized workspace. |
| Build, run and tests | Discover configurations, build, launch or attach, and run tests. Return diagnostic locations, test outcomes, and failure artifacts. |
| Emulators and adapters | Manage bucket and installed-bundle lifecycle, select presets and versions, list sessions and capabilities, and control supported emulator lifecycle operations. |
| Debugging and guest inspection | Read registers and memory, set supported breakpoints and watchpoints, inspect disassembly and trace, step execution, and expose optional guest services. |
| Video, audio, input and replay | Capture the display, read supported stream chunks, inspect playback, set host volume, route scoped input, and seek eligible recordings. |
| Media, snapshots and diagnostics | List, mount, and eject media, manage supported snapshots, and read console, logs, and diagnostic artifacts. |
| Agents and operations | List, launch, and attach configured agents where supported. Inspect permission or operation outcomes and query, wait for, or cancel running operations. |

Backend capabilities, workbench state, and client grants govern actual tool availability. Return a structured unavailable result when a required capability or context is absent. Discovery and capability changes use the SDK's supported MCP notifications where negotiated.

## Hosting and results

- **Attach.** A lightweight stdio entry point attaches to the selected running IDE instance for CLI clients. Desktop clients can attach to a loopback Streamable HTTP endpoint on the same instance. Both transports share the tool registry and dispatch service. The HTTP host validates the client, token, and Origin as applicable. [MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- **Scope.** Bind every client to its authorized IDE instance, workspace and capabilities. Validate arguments, resolve paths and IDs in that context, and apply command eligibility before dispatch. The configured adapter or emulator host enforces its own authorization. Audit tool name, actor, context, operation ID, and outcome.
- **Return.** Provide structured results with a concise summary, contextual IDs, and stable errors. Report revisions when relevant. Use explicit expected revisions or context on mutations that depend on prior state. Carry request cancellation through the dispatcher and reuse the same operation identity for retries where the feature supports it.
- **Long-running work.** Report progress for clients that request it. A build, launch, Git operation, or bundle install can return an operation ID with status, result, and cancel tools. Clients that negotiate MCP Tasks can use the SDK extension for task status and result retrieval.
- **Large results.** Read logs, disassembly, or supported media streams through bounded tools with cursor or stream handles. Image captures can return MCP image content. Resources can provide alternate access to the same data.

Verify tool discovery and invocation through both transports. Compare UI and MCP outcomes for the same typed operation. Verify ViewModel context schema parity, live context resolution, scoped authorization, ambiguous or stale targets, capability changes, progress and cancellation, and every registered feature area. Track this feature under `FR-RETRO-015`, `TR-RETRO-MCP-015`, and `TEST-RETRO-015`.
