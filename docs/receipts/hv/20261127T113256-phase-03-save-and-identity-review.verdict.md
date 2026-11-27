# Retro IDE Phase 03 BDPv4 verdict

**03A `EXEC-TODO-008` — DISAGREE. Accuracy 96/100. Completeness 95/100.**  
**Overall — DISAGREE. Accuracy 96/100. Completeness 95/100.**

I reviewed exact revision `ae2199a662c1737898f5f2f1668e4ccc5c8d33a1` using the read-only `phase03_evidence` packet and applied `MEMORY-HV-100` version 6. The repair passes the requested hook, cross-checkout branch, cancellation, and submodule replacement cases. Two source findings still prevent 03A from meeting the 98/98 gate. No accepted task showed a regression in its accepted requirements. 03A and Phases 04–08 remain open. [Source identity][identity] · [Plan][plan] · [QBrain scope][scope] · [HV rules][memory]

## Findings that block 03A

1. **FAIL — Built-in editor Save can change an effective Git endpoint while the transfer holds the gate.** The live branch-switch fixture uses an ordinary configuration file from a different checkout. That same file can be opened and saved through the workbench or `editor_save_document`. Save replaces the file without acquiring the Git gate. The named transfer checks the effective configuration and then starts a separate Git process with the remote name. A Save before the subprocess reads configuration can therefore change the endpoint after the final check. A Save after the subprocess connects can still change the effective endpoint while the transfer is live. This is a source-confirmed serialization gap. The packet has no live Save-versus-transfer case, so the endpoint actually contacted in that race is **UNKNOWN**. Affected criteria are `ac-git-review-and-publish`, `ac-git-state-consistency`, and the Git portions of `ac-mcp-context-and-outcomes` and `ac-test-mcp-scope`. [Cross-checkout include fixture][include] · [Save CQRS entry point][save] · [File replacement][replace] · [Fetch check and launch][fetch]

2. **FAIL — A queued named transfer does not revalidate checkout identity after acquiring the gate.** Fetch, push, and pull discover and compare the selected checkout before authorization. After waiting, they recheck configuration and, for push and pull, refs using the captured administrative path. If the worktree now points to a different Git directory and the old directory still exists, those checks can succeed against the old directory. The submodule path explicitly rediscovers after acquiring the gate and rejects a changed child. The named-transfer path lacks that check. This is a source finding against `ac-git-machine-state` and the Git expected-version portion of `ac-mcp-context-and-outcomes`. The packet has no live named-transfer result for this replacement case. [Initial discovery][discovery] · [Fetch after acquisition][fetch] · [Push/pull checks][sync] · [Explicit Git directory arguments][nativegit] · [Submodule recheck][submodule]

## Requirements per change

Numbered groups refer to the FR, TR, and TEST records in the plan. PASS applies to the observed behavior in each row. Complete production CQRS proof was sufficient where the criterion permits it. [Plan traceability][plan] · [Effective QBrain criteria][criteria]

| Change or affected path | Requirements in flux and shared tasks | Result |
|---|---|---|
| Process-wide gate for native commit/amend and cross-checkout branch changes | Git 013 publish and consistency, MCP 015 expected versions. Shared with 01C and 03C. | **PASS** for the three repaired cases. The original red had three intended failures and six passing controls. The focused green passed all 82. Both exact host suites passed all three cases. The test held a real native fetch open, read the unchanged endpoint during the transfer, then observed the mutation succeed and the endpoint change after release. [Red][red] · [Green][green] · [Live assertions][hooks] · [Linux TRX][li] · [Windows TRX][wi] |
| Direct remote edits, earlier branch and submodule overlaps, gate acquisition and release | Git 013 publish, advanced workspace and consistency. Shared with 01C and 03C. | **PASS** for direct remote edits against fetch, pull and push, and for the original branch and submodule overlaps. The original reds discriminated both repairs. The single cancellation-aware lease releases through disposal. Clone/init hold it through install or discard. Branch, upstream, recovery, reset, stash, patch application, tags, worktree operations and LFS acquire it. Manual terminal and external commands use native Git locking. [Direct-edit red][directred] · [Branch/submodule red][endpointred] · [Gate][gate] · [Repository install lease][provision] · [Documented boundary][gitdocs] |
| Queued cancellation and captured submodule replacement | Git 013 machine state, recovery and extensions, MCP 015 operation outcomes. Shared with 02A and 03C. | **PASS** for the supplied cases. The canceled fetch opened no socket and a fresh fetch acquired the gate. Repointing the captured child to different administrative metadata returned `git_submodule_changed` and preserved the replacement endpoint. Replacement that reuses the same administrative paths is **UNKNOWN**. The comparison is path-based. [Queued assertions][queued] · [Identity comparison][nativegit] · [Linux TRX][li] · [Windows TRX][wi] |
| Save and post-wait identity boundaries | Git 013 state consistency, machine state and publish, MCP 015 expected versions and Git scope. Shared save contracts checked under 02B and 03B. | **FAIL** for the two source findings above. The gate also releases after native file/hunk staging preparation and before final index publication, which uses the native index lock. The packet does not establish a live staging-publication overlap with a configuration-sensitive transfer. [Index preparation][indexprep] · [Index publication][indexpub] |
| Windows path equivalence and native Git behavior | Git 013 context, machine state, native host, terminal and extensions, MCP 015 parity. Shared with 01C, 02A and 03C. | **PASS** for tested paths and routes. Windows exercised forward-slash worktree paths through HTTP and stdio. Native smart HTTP credentials, denial, secret exclusion, SSH commit signing verified by Git, configured hooks, LFS, worktrees, and terminal prompts and refresh passed. Live SSH remote transport is **UNKNOWN**. [Path normalization][nativegit] · [Authentication and signing assertions][auth] · [Windows TRX][wi] · [Windows native][wn] |

## 03A criterion results

| Requirement | Evidence and disposition |
|---|---|
| `FR-RETRO-013` | **PASS** `ac-git-context`, `ac-git-integration-workflows`, and `ac-git-advanced-workspace` for discovery and selection, repo badges, recovery, worktrees, submodules, installed LFS and the scoped terminal. **FAIL** `ac-git-review-and-publish` at the reviewed-endpoint boundary. The history, blame, hunk, commit/amend, branch, tag, upstream and ordinary remote workflow cases passed. [Linux TRX][li] · [Windows TRX][wi] · [Windows native Git workflow][wngit] |
| `TR-RETRO-GIT-013` | **FAIL** `ac-git-state-consistency` and `ac-git-machine-state` at the Save and queued identity boundaries. **PASS** `ac-git-operation-recovery` and `ac-git-terminal`. **PASS** the exercised native-host behavior under `ac-git-native-host`, with live SSH network authentication **UNKNOWN**. [Save][save] · [Initial identity check][discovery] · [Native invocation][nativegit] · [Linux native terminal][lnterminal] |
| `TEST-RETRO-013` | **PASS** `ac-test-git-multiple-repos`, `ac-test-git-stage-and-push`, `ac-test-git-recovery`, and `ac-test-git-extensions` for their listed scenarios. These include isolated indexes and literal paths, configured authentication and signing, divergence and rejected updates, stale hunks and dirty buffers, linked worktrees, submodules, LFS, and external refresh. The two residual races are outside the exercised scenarios. [Linux TRX][li] · [Windows TRX][wi] · [Native Git workflow][wngit] |
| Git and terminal portions of MCP 015 | **PASS** same-instance typed invocation and the tested UI/HTTP/stdio parity, workspace and capability denials, terminal scope, and operation correlation. **FAIL** the Git expected-version and stale-review portions of `ac-mcp-context-and-outcomes` and `ac-test-mcp-scope`. Full inventory, negotiated Tasks, and later feature coverage stay open. [Effective MCP criteria][criteria] · [Live operation assertions][operation] · [Terminal scope proof][terminaltest] |

## Requirements per task and accepted-task regressions

| Task | Requirements | Result |
|---|---|---|
| 01A `EXEC-TODO-001` | Desktop 001 | **No regression.** Native focus, float and redock passed on both hosts. The retained X11/XTEST run covers physical gestures. [Linux native][ln] · [Windows native][wndock] · [Retained X11][x11] |
| 01B `EXEC-TODO-002` | Command 007 | **No regression.** Shared command routing, originating-view delivery, and correlated cancellation and stale rejection passed. [Linux TRX][li] · [Windows TRX][wi] |
| 01C `EXEC-TODO-003` | MCP 015 workbench context, dispatch and transports | **No regression.** Schema and same-instance focus, foreign workspace denial, and shutdown revocation passed. The stale Git review findings belong to 03A. [Windows production attachment][wnattach] · [Linux native][ln] |
| 01D `EXEC-TODO-004` | Desktop 001, Command 007, Localization 008 | **No regression.** Layout restoration, native Wait cursor scope, overlapping outcomes, and responsive commands passed. [Linux TRX][li] · [Windows native][wndock] |
| 02A `EXEC-TODO-005` | Explorer 012, TEST 015 | **No regression.** Mixed-root routing, Reveal, watcher refresh and retired-listing denial passed. [Linux TRX][li] · [Windows TRX][wi] |
| 02B `EXEC-TODO-006` | Editor 011, Command 007, FR/TEST 015 | **No regression.** Independent buffers, save after a later edit or close, and diagnostic navigation passed. The new Git overlap finding concerns coordination with Save. [Linux TRX][li] · [Windows TRX][wi] |
| 02C `EXEC-TODO-007` | Localization 008, TEST 015 | **No regression.** Pack rejection and fallback, live labels and RTL, shortcuts, and persistence passed. [Linux TRX][li] · [Windows native][wn] |
| 03A `EXEC-TODO-008` | FR/TR/TEST Git 013 and Git/terminal MCP 015 | **DISAGREE, 96 accuracy and 95 completeness.** Open. |
| 03B `EXEC-TODO-009` | LSP 014 and language MCP 015 | **No regression.** Both hosts observed exact committed text in `didSave` at first synchronization and before close, one coalesced save, and monotonic document versions. Installed clangd and rust-analyzer passed on both hosts. The ten accepted LSP criteria retain their status. [Save assertions][lspsave] · [Linux TRX][li] · [Windows TRX][wi] |
| 03C `EXEC-TODO-010` | MCP 015 scoped operation handles | **No regression.** HTTP and stdio correlated the same handle for its owning grant. Native success, cancellation, and `git_fetch_failed` remained distinct. A foreign grant could neither retrieve the result nor cancel. [Live outcome assertions][operation] · [Linux TRX][li] · [Windows TRX][wi] |
| Phase 04 | Bundle 009, Adapter 010, Machine 017 | Open, outside this review. |
| Phase 05 | Desktop 001, Profile 002, Command 007, Bundle 009, Adapter 010, Editor 011, MCP 015, Machine 017 | Open, outside this review. |
| Phase 06 | Debug 004, Adapter 010, Editor 011, MCP 015, CPU 016, Machine 017 | Open, outside this review. |
| Phase 07 | AV/Input 003, Workflow 005, Adapter 010, MCP 015 | Open, outside this review. |
| Phase 08 | Desktop 001, Agent 006, Adapter 010, MCP 015 | Open, outside this review. |

## Exact-revision evidence and Rule 6

| Verification | Result |
|---|---|
| Both full managed suites | **PASS.** LAB-OMARCHY and PAYTON-LEGION2 each ran the same 271 integration and 2 unit cases, with zero failures and zero skips. The first Windows fixture run failed opening the hidden `.git` pointer. The in-place file-open fixture passed its standalone rerun and the exact full suite. [Linux integration][li] · [Linux unit][lu] · [Windows integration][wi] · [Windows unit][wu] · [Initial fixture failure][fixturefail] · [Exact fixture rerun][fixturepass] |
| Both native runtimes | **PASS.** Linux rendered PowerShell on display 468, emitted its cursor-position reply, answered native credentials and received the exact ref. Windows Win32 RemoteControl exercised the terminal, prompts, Git workflows and production attachments. [Linux native receipt][lnresult] · [Linux terminal][lnterminal] · [Windows native][wn] |
| Linux full compile and QBrain trace | **PASS.** Release Compile, ValidateTraceability and Test succeeded. The trace verified 182 links, 32 documented public contracts and 32 distinct criteria against the supplied QBrain export. Nuke logged a suppressed telemetry assembly error before the successful compile. [Full Linux log][linuxbuild] · [Trace][trace] |
| Audited Windows cross-build and binary identity | **PASS on retained evidence.** Unit, Integration and Native cross-builds on Linux reported zero errors. The default-audit manifest has 237 unique paths. Its recomputed digest, both Windows TRX run IDs and hashes, and the native-log hash match the PAYTON-LEGION2 verifier. [Unit build][crossu] · [Integration build][crossi] · [Native build][crossn] · [Manifest][manifest] · [Windows verifier][verifier] |
| Linux Git 2.53.0 | **PASS.** All 21 cases passed, including the hook/include cases, queued cancellation, child replacement and earlier exclusion cases. [Git version][gitver] · [Original TRX][legacy] |

I recomputed 57 retained receipt digests and found no mismatches. The local command policy blocked an independent hostname check and a fresh test run. The original TRXs, native logs, and verifier provide independent evidence of the recorded PAYTON-LEGION2 run. Independent byte verification of the source archive and transferred binaries remains **UNKNOWN** because the reader exposes their identity and manifest receipts. Under Rule 6, those operational limits carry no score deduction. The scores reflect the two product findings and their missing live boundary cases. The packet and QBrain state stayed read-only. [Retained receipts][receipts] · [Windows verifier][verifier] · [Source identity][identity]

[plan]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/docs/plans/PLAN-RETROIDE-001.md:104
[identity]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/source-identity.json
[scope]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/qbrain-execution-scopes.json
[memory]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/hv-memory.json
[criteria]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/qbrain-effective.yaml
[receipts]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/retained-receipts.json
[include]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.Hooks.cs:79
[save]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Workspaces/ExplorerFeatures.cs:49
[replace]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Workspaces/PreparedDocumentSave.cs:39
[fetch]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/GitRemoteOperations.cs:77
[discovery]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/GitRemoteOperations.cs:193
[sync]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/GitRemoteOperations.Sync.cs:184
[nativegit]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/NativeGit.cs:95
[submodule]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/GitSubmoduleOperations.cs:33
[red]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/repairs/native-hooks-and-includes/red.trx:12
[green]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/repairs/native-hooks-and-includes/green.trx:510
[hooks]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.Hooks.cs:44
[directred]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/baseline/baseline/repairs/remote-transfer-race/red.trx:8
[endpointred]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/baseline/repairs/effective-endpoint-overlap/red.trx:8
[gate]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/GitConfigurationGate.cs:8
[provision]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/PreparedGitRepository.cs:58
[gitdocs]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/docs/git-integration.md:51
[queued]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.Queued.cs:36
[indexprep]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/PreparedGitIndex.cs:19
[indexpub]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/src/RetroIde.Application/Git/PreparedGitIndex.cs:52
[auth]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/GitDiffIndexTests.Authentication.cs:17
[operation]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/Phase03OperationContractTests.cs:54
[terminaltest]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/TerminalWorkbenchTests.cs:77
[lspsave]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/tests/RetroIde.IntegrationTests/LanguageServerIntegrationTests.QueuedSave.cs:65
[li]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-Integration.trx:8
[wi]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Integration.trx:8
[lu]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-Unit.trx:30
[wu]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Unit.trx:30
[wn]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Native.stdout.log
[wngit]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Native.stdout.log:9
[wndock]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Native.stdout.log:37
[wnattach]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/Native.stdout.log:42
[ln]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-native/pwsh-nuke-native.log:104
[lnterminal]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-native/pwsh-nuke-native.log:101
[lnresult]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-native/pwsh-result.json
[x11]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/baseline/baseline/previous-review/prior/linux/x11-gestures.log:3
[fixturefail]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/repairs/native-hooks-and-includes/initial-windows-fixture.trx:55
[fixturepass]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/verified-fixture.trx:24
[linuxbuild]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/linux-full.log:39
[trace]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/live-traceability.log
[crossu]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/audited-cross-Unit.log:9
[crossi]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/audited-cross-Integration.log:1429
[crossn]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/audited-cross-Native.log:103
[manifest]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/binaries.json
[verifier]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/windows/verified.json
[gitver]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/legacy-git-version.txt
[legacy]: /F:/GitHub/retro-hv-phase03-ae2199a662c1/workspace/evidence/phase03/current/legacy-git.trx:144