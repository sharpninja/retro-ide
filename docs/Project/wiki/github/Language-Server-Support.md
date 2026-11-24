# Language server support

Retro IDE will act as a Language Server Protocol (LSP) client for the AvaloniaEdit source editor. A workspace can use different configured servers for different languages and roots, including servers that also work with VS Code. Use LSP 3.18 as the reference and negotiate capabilities for interoperability with 3.17 servers. [LSP 3.18 specification](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.18/specification.md) · [VS Code language server guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide)

## Configuration and lifecycle

Server registrations identify an installed executable, arguments, language IDs and file patterns, root discovery rules, and initialization settings. Workspaces select server registrations and can override language settings per root. Select the server using the document language and containing root so a multi-root workspace can use different toolchains. Use one compatible server session for a group of roots when it supports workspace folders. Otherwise start sessions per root.

Start the selected process asynchronously in its workspace context. The initial transport is LSP's framed JSON-RPC over standard input and output. Standard error goes to a language-server output channel. Exchange `initialize` and `initialized` before opening documents. Handle supported dynamic capability registration, configuration, and workspace folder updates. Expose server state and restart in the IDE. On shutdown send `shutdown` then `exit`, and release the process and subscriptions.

The first conformance targets are standalone clangd for C and C++ and rust-analyzer for Rust. Registrations can support additional languages and assembly dialects. A file without a configured server opens with the editor's local syntax highlighting or plain text. [clangd client setup](https://clangd.llvm.org/installation) · [rust-analyzer manual](https://rust-analyzer.github.io/book/other_editors.html)

## Editor features

| Area | IDE behavior |
| --- | --- |
| Editing assistance | Completion and supported completion-item resolve, hover, signature help, snippets, semantic highlighting, and inlay hints. |
| Navigation | Go to definition, declaration, type definition, or implementation when supported. Find references and use document and workspace symbols. Open a result in the corresponding editor tab. |
| Diagnostics | Show supported push or pull diagnostics in the source editor and Problems view, with their language-server origin distinct from build and emulator diagnostics. |
| Code changes | Document or range formatting, rename, quick fixes, code actions, and workspace edits when advertised. Resolve and validate edits before dispatching them to the affected documents through CQRS. |

Effective features follow the server's advertised capabilities for the document. Project settings and commands use the existing localized command catalog. The registered editor view's controller supplies the captured document and workspace context. CQRS handlers query the language service, return results through the originating view's Avalonia dispatcher, and handle edits through the existing document workflow.

## Synchronization and consistency

- Derive file URIs and ranges from the workspace and the active editor buffer. Use the negotiated position encoding, including the LSP UTF-16 default. Send open, change, save, and close notifications according to the server's document-sync capability. Keep document versions monotonic within a session. Synchronize unsaved editor content before requesting language features that depend on it.
- Correlate each request with its server session, workspace, document URI, and document version. Cancel superseded requests where supported. Ignore late responses for a closed document, replaced server session, or stale document version. Keep optional diagnostic versions when provided and clear a server's diagnostics when its session or document closes.
- Server-originated operations such as `workspace/applyEdit` enter the main controller as typed requests. Resolve all targets, validate document versions and requested resource changes, and apply supported edits through the document model. Use the open buffer for an open file. Advertise resource operation and failure-handling capabilities that the client can honor. Present failures and any supported multi-file preview through the workbench.
- If a server exits or becomes unavailable, keep the editor usable and expose the failure in Language Servers and the output channel. A restart negotiates capabilities again and reopens the current editor buffers. Deliver workbench and editor updates on the Avalonia UI thread.

## Verification

Exercise the LSP client on Windows and Linux with a deterministic server fixture for initialization, capabilities, document versions, cancellation, multi-root routing, diagnostics, and workspace edits. Include a Unicode position-encoding fixture, a dirty buffer, and a server failure and restart. Run real-server checks for clangd and rust-analyzer against small projects. Track this feature under `FR-RETRO-014`, `TR-RETRO-LSP-014`, and `TEST-RETRO-014`.
