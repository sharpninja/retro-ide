# Retro IDE Phase 03 independent verdict

**Revision `60e32c1c5a60cfbd52f934b1d789b3cd802d64fb`**

| Scope | Verdict | Accuracy | Completeness |
|---|---|---:|---:|
| 03A · `EXEC-TODO-008` | **DISAGREE** | 97/100 | 96/100 |
| Overall | **DISAGREE** | 97/100 | 96/100 |

The real editor Save race, all three queued named transfers, and the staging-publication race pass on the candidate. **Branch mutations and reviewed patch application still have a post-wait checkout-identity gap.** I found no regression in the accepted Phase 01, 02, 03B, or 03C task criteria. 03A and Phases 04–08 stay open. I applied `MEMORY-HV-100` v6 and kept the packet and QBrain state read-only. [Source identity][identity] · [Plan][plan] · [QBrain execution scopes][scope] · [HV rules][rules]

## Blocking finding

**FAIL — A queued branch mutation or patch application can act through obsolete Git metadata.** Branch create and switch, branch rename and delete, and patch application discover the checkout during preparation. After acquiring the gate, the branch paths recheck refs through the captured `GitWorktree`. Patch application rechecks its preimage through that same captured object, and its index route can publish to the captured index. These native commands explicitly supply `--git-dir` and `--work-tree`. Their post-wait checks can pass when the `.git` pointer has changed and the old metadata and preimages are still valid. A branch rename can therefore update the old repository’s refs and branch configuration. An index patch can publish into the old index. [Branch create and switch][branch] · [Branch rename and delete][maintenance] · [Patch apply][patch] · [Native Git arguments][nativeargs]

The original named-transfer red exercises the decisive premise: after pointer replacement, the old metadata still supported explicit native commands, including pull’s checked-out branch validation, and the subprocess connected. The candidate closes that boundary for fetch, pull, push, remote edits, and upstream changes by rediscovering after acquisition. **UNKNOWN** is the runtime outcome of the analogous branch or patch race. The source establishes the missing check, and the packet contains no live replacement case for those paths. This affects Git machine state, branch and patch workflows, and MCP stale-context rejection. [Original red][redindex] · [Replacement assertions][swap] · [Remote identity recheck][remote] · [Upstream publication][upstream]

## Requirements per change

| Change or affected path | Requirements and shared tasks | Result |
|---|---|---|
| Editor Save takes the process-wide lease through publication | Git 013 state consistency and publish, Editor 011 Save, LSP 014 document delivery. Shared with 02B and 03B. | **PASS.** Two workbench sessions used separate checkouts and a real loopback Git connection. The editor submitted Save over stdio while fetch was held. Native Git saw the original included endpoint during fetch and the replacement only after release. The red read the replacement during fetch. The save still commits through the originating document controller and delivers the committed snapshot to LSP. [Live Save assertions][savecase] · [Save preparation][saveprepare] · [Document publication][savecommit] · [Red][redindex] |
| Named fetch, pull, and push rediscover after waiting | Git 013 machine state and publish, MCP 015 expected versions and scope. Shared context contracts with 01C and 03C. | **PASS.** Each queued call remained pending before replacement. The fixture verified the new `.git` resolution and that the old administrative directory still existed. All three red calls opened a second native connection. Each fixed call returned `git_repository_unavailable` with no second connection on both hosts. Same-path replacement remains **UNKNOWN** because identity comparison uses paths. [Replacement assertions][swap] · [Red][redindex] · [Linux TRX][li] · [Windows TRX][wi] · [Identity comparison][nativeargs] |
| File and hunk stage or unstage transfer the lease to the prepared index | Git 013 state consistency and staging, MCP 015 scoped mutation. | **PASS.** The live held-index case excludes fetch until native index publication and verifies the staged path. The original red connected before publication. Source confirms release on failed or canceled preparation, hunk verification failure, and discarded CQRS outcomes. A returned preparation stays owned through publish or rejection, and cleanup releases the lease in `finally`. Separate live socket observations for each cleanup exit are **UNKNOWN**. [Index race][indexcase] · [Index lease and cleanup][index] · [Hunk preparation][hunks] · [CQRS disposal][dispatch] |
| Earlier hooks, cross-checkout branch writes, direct remote edits, submodule operations, and queued cancellation | Git 013 consistency, recovery, and extensions. Shared outcome contracts with 03C. | **PASS** for the exercised cases. Native pre-commit hooks on commit and amend and branch writes to an included file waited behind fetch. Direct remote edits and submodule sync retained their exclusion. A canceled queued fetch opened no connection and a subsequent fetch acquired the gate. Replacing a captured submodule returned `git_submodule_changed` and preserved its endpoint. [Hook assertions][hooks] · [Queued assertions][queued] · [Linux TRX][li] · [Windows TRX][wi] |
| Post-wait branch and patch checks | Git 013 publish, integration workflows, and machine state. Git portions of MCP 015 outcomes and scope. | **FAIL.** These built-in operations retain an obsolete administrative target across the wait. Their live replacement behavior is **UNKNOWN**. [Branch maintenance][maintenance] · [Patch apply][patch] · [Native Git arguments][nativeargs] |

## 03A criterion results

| Requirement | Disposition |
|---|---|
| `FR-RETRO-013` | **PASS** `ac-git-context` and `ac-git-advanced-workspace`. **FAIL** `ac-git-review-and-publish` for branch publication and `ac-git-integration-workflows` for patch application under checkout replacement. Ordinary branch, patch, conflict, history, commit, and transfer workflows passed. [QBrain criteria][criteria] · [Windows native][wn] · [Branch maintenance][maintenance] · [Patch apply][patch] |
| `TR-RETRO-GIT-013` | **FAIL** `ac-git-machine-state` at the captured-checkout boundary. **PASS** `ac-git-state-consistency` for the exercised lock, serialization, external refresh, and stale-hunk behaviors, plus `ac-git-operation-recovery` and `ac-git-terminal`. `ac-git-native-host` passes for the exercised native arguments, HTTP credentials and denial, secret exclusion, hooks, and SSH commit signing. Live SSH remote authentication is **UNKNOWN**. [Git criteria][gittr] · [Authentication and signing][auth] · [Linux TRX][li] · [Linux native][ln] |
| `TEST-RETRO-013` | **PASS** the mapped cases for `ac-test-git-multiple-repos`, `ac-test-git-stage-and-push`, `ac-test-git-recovery`, and `ac-test-git-extensions`. The evidence covers literal and unusual paths, isolated indexes, selective staging, authentication and signing, conflict recovery, installed LFS, linked worktrees, submodules, and terminal refresh. [Test criteria][gittest] · [Linux TRX][li] · [Windows TRX][wi] · [Windows native][wn] |
| Git and terminal portions of `FR-RETRO-015`, `TR-RETRO-MCP-015`, and `TEST-RETRO-015` | **PASS** the exercised same-instance, typed dispatch, UI/stdio/HTTP parity, grant isolation, terminal scope, and operation outcomes. **FAIL** the Git stale-context portion of `ac-mcp-context-and-outcomes` and `ac-test-mcp-scope`. Full inventory, negotiated Tasks, and bounded provider results stay open. [MCP criteria][mcptr] · [Scope test criterion][mcptest] · [Live operation contract][operation] |

I credited complete production CQRS proof where the criterion allowed it. Accuracy reflects the surviving wrong-checkout publication paths. Completeness reflects the missing live branch and patch replacement proof.

## Requirements per task and regression results

Requirement numbers refer to the FR/TR/TEST families in the plan. [Plan traceability][traceplan]

| Task | Requirements | Result |
|---|---|---|
| 01A · `EXEC-TODO-001` | Desktop 001 | **No regression.** Exact XID and HWND focus, float, and redock passed. The earlier X11/XTEST receipt covers physical gestures. [Linux native][ln] · [Windows native][wn] · [XTEST][x11] |
| 01B · `EXEC-TODO-002` | Command 007 | **No regression.** Shared commands, originating-view delivery, and correlated cancellation and stale rejection passed. [Linux TRX][li] · [Windows TRX][wi] |
| 01C · `EXEC-TODO-003` | MCP 015 workbench context and transports | **No regression.** Live context, same-instance attachment, foreign workspace denial, and registration isolation passed. [Linux native][ln] · [Windows native][wn] |
| 01D · `EXEC-TODO-004` | Desktop 001, Command 007, Localization 008 | **No regression.** Layout restoration, scoped Wait, overlapping outcomes, and available commands passed. [Linux native][ln] · [Windows native][wn] |
| 02A · `EXEC-TODO-005` | Explorer 012, scoped MCP 015 | **No regression.** Mixed-root routing, Reveal, refresh, and retired-listing rejection passed. [Linux TRX][li] · [Windows native][wn] |
| 02B · `EXEC-TODO-006` | Editor 011, Command 007, scoped MCP 015 | **No regression.** Save stayed bound to its original document across later edits and close. Failed Save retained the buffer. Native editor state survived float and redock. [Linux TRX][li] · [Windows TRX][wi] · [Windows native][wn] |
| 02C · `EXEC-TODO-007` | Localization 008, scoped MCP 015 | **No regression.** Pack fallback and validation, live RTL, stable shortcuts, and persistence passed. [Linux TRX][li] · [Windows native][wn] |
| 03A · `EXEC-TODO-008` | Git 013 and Git/terminal MCP 015 | **DISAGREE · 97 accuracy, 96 completeness.** Open. [QBrain scope][scope] |
| 03B · `EXEC-TODO-009` | LSP 014 and language MCP 015 | **No regression.** Both hosts passed exact-text `didSave` at initial synchronization and before close, one coalesced save, monotonic versions, and installed clangd and rust-analyzer. [Queued Save][lspsave] · [Coalesced Save][coalesced] · [Linux TRX][li] · [Windows TRX][wi] |
| 03C · `EXEC-TODO-010` | MCP 015 scoped operation handles | **No regression.** Both transports agreed on operation identity and native success, cancellation, and failure. A foreign grant could neither retrieve nor cancel the operation. [Live operation contract][operation] · [Linux TRX][li] · [Windows TRX][wi] |
| Phase 04 | Bundle 009, Adapter 010, Machine 017 | Open, outside adjudication. |
| Phase 05 | Desktop 001, Profile 002, Command 007, Bundle 009, Adapter 010, Editor 011, MCP 015, Machine 017 | Open, outside adjudication. |
| Phase 06 | Debug 004, Adapter 010, Editor 011, MCP 015, CPU 016, Machine 017 | Open, outside adjudication. |
| Phase 07 | AV/Input 003, Workflow 005, Adapter 010, MCP 015 | Open, outside adjudication. |
| Phase 08 | Desktop 001, Agent 006, Adapter 010, MCP 015 | Open, outside adjudication. |

## Exact-revision receipts and Rule 6

- **PASS · Two-host managed and native proof.** LAB-OMARCHY and PAYTON-LEGION2 each passed 276 integration tests and two unit tests with zero failures or skips. All five targeted cases also passed in the 16-case Windows fixture run. Linux native PowerShell RemoteControl completed on display 469, and the transferred Windows runner exercised real HWNDs. The focused green passed 146 tests with two installed-LSP skips. Both installed cases passed in each full host run. [Linux build and managed log][lfull] · [Windows verifier][wv] · [Windows fixture][wf] · [Linux native result][lnr] · [Windows native][wn] · [Focused green][green]
- **PASS · Compatibility, audited builds, and digests.** Git 2.53.0 passed 26/26, including all five targeted cases. The Linux compile and the Linux-to-Windows Unit, Integration, and Native cross-builds succeeded. I recomputed 34 retained receipt SHA-256 digests with zero mismatches, including the Windows manifest and run outputs. The Windows verifier binds 237 unique binary manifest entries to this revision. The retained red index test-source hash matches the candidate test source. [Git version][legacyver] · [Git 2.53 TRX][legacy] · [Linux compile][lfull] · [Windows cross-build][wc] · [Binary manifest][wm] · [Receipt manifest][receipts] · [Index test-source hash][indexhash]
- **PASS · Scoped QBrain trace.** The compiled check reported 185 test links through Phase 03, 32 documented public contracts, and 32 distinct criteria. The recomputed export digest matches the trace log. The supplied execution scopes record 03A as Implementing and 03B/03C as Complete. [Trace log][trace] · [QBrain export][criteria] · [Execution scopes][scope]
- **Rule 6 · Operational limits.** The review shell rejected hostname and test execution. The retained verifier and original TRXs establish the recorded PAYTON-LEGION2 runs. Independent confirmation of this session’s hostname and byte-level rehashing of the source archive and transferred binaries remain **UNKNOWN**. Those limits carried no score deduction. [Source identity][identity] · [Windows verifier][wv] · [HV rules][rules]

[plan]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/docs/plans/PLAN-RETROIDE-001.md:104
[traceplan]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/docs/plans/PLAN-RETROIDE-001.md:153
[identity]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/source-identity.json
[scope]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-execution-scopes.json
[rules]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/hv-memory.json
[branch]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitBranchOperations.cs:83
[maintenance]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitBranchOperations.Maintenance.cs:93
[patch]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitPatchOperations.cs:61
[nativeargs]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/NativeGit.cs:95
[remote]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitRemoteOperations.cs:207
[upstream]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitRemoteOperations.Upstream.cs:16
[savecase]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.SaveAndIdentity.cs:16
[swap]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.SaveAndIdentity.cs:66
[indexcase]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.SaveAndIdentity.cs:116
[saveprepare]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Workspaces/ExplorerFeatures.cs:54
[savecommit]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Workbench/DocumentController.cs:242
[index]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/PreparedGitIndex.cs:29
[hunks]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/Git/GitFileOperations.Hunks.cs:21
[dispatch]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/src/RetroIde.Application/WorkbenchDispatch.cs:428
[hooks]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.Hooks.cs
[queued]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitRemoteConcurrencyTests.Queued.cs:16
[auth]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/GitDiffIndexTests.Authentication.cs:17
[operation]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/Phase03OperationContractTests.Live.cs:20
[lspsave]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/LanguageServerIntegrationTests.QueuedSave.cs:15
[coalesced]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/tests/RetroIde.IntegrationTests/LanguageServerIntegrationTests.CoalescedSave.cs:15
[criteria]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-effective.yaml:275
[gittr]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-effective.yaml:594
[gittest]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-effective.yaml:1033
[mcptr]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-effective.yaml:687
[mcptest]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/qbrain-effective.yaml:1076
[li]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/linux-Integration.trx
[wi]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/Integration.trx
[ln]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/linux-native/pwsh-nuke-native.log:43
[lnr]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/linux-native/pwsh-result.json
[wn]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/Native.stdout.log
[x11]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/baseline/baseline/baseline/previous-review/prior/linux/x11-gestures.log:3
[lfull]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/linux-full.log:1577
[wv]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/verified.json
[wf]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/verified-fixture.trx
[wc]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/audited-cross-Integration.log:732
[wm]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/windows/binaries.json
[legacyver]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/legacy-git-version.txt
[legacy]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/legacy-git.trx
[trace]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/current/live-traceability.log
[receipts]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/retained-receipts.json
[redindex]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/repairs/source-save-and-worktree-identity/red-with-index.trx
[green]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/repairs/source-save-and-worktree-identity/green.trx
[indexhash]: /F:/GitHub/retro-hv-phase03-60e32c1c5a60/workspace/evidence/phase03/repairs/source-save-and-worktree-identity/index-test-source-sha256.txt