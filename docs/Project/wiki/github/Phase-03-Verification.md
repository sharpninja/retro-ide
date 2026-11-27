# Phase 03 acceptance

Retro IDE Phase 03 passed independent HV at **99/100 accuracy and 98/100 completeness** on revision `ca9ae78c4d90bd16caba4e448d4596b2e2a22cda`. GPT-6.2-SOL High reviewed the frozen candidate on LEGION2 on November 27, 2026. The acceptance threshold was 98 in both dimensions. [Independent verdict](hv/20261127T132143-phase-03-accepted.verdict.md) · [Phase 03 plan](../plans/PLAN-RETROIDE-001.md#03--git-terminal-and-language-servers)

| Task | Decision | Evidence |
| --- | --- | --- |
| 03A · `EXEC-TODO-008` | AGREE 99/98 | Native Git and the repository terminal, scoped MCP calls, queued writes rejected when the selected checkout changes, and concurrency through editor and index publication |
| 03B · `EXEC-TODO-009` | AGREE 99/99 | Framed language-server synchronization, including first-save and save-before-close delivery, with fixture, clangd, and rust-analyzer testing |
| 03C · `EXEC-TODO-010` | AGREE 99/99 | Same-instance operation identity, typed success, failure and cancellation, and grant-scoped result privacy across HTTP and stdio |

Linux and LEGION2 each passed 284 managed tests and the native RemoteControl run. Windows executed 237 hash-verified binary files built on Linux with NuGet audit enabled. Linux passed all 32 selected Git 2.53 compatibility cases. The Nuke traceability run verified 187 test links, 32 public contracts, and 32 criteria against the QBrain export.

The [Phase 03 C4 snapshot](../architecture/structurizr/versions/implementation-03-git-terminal-language-servers/manifest.json) pins the tested revision, the independent verdict hash, and 28 validated views.

The earlier build reproduced six operations that wrote to a checkout after its Git metadata was replaced. The accepted build rejects branch create, switch, rename and delete and both reviewed patch layers before mutation. Each test checks that the old refs, HEAD, index, and targeted file remain unchanged. The same acquisition check covers built-in writes to existing worktrees. [Git integration](../git-integration.md)

The tests exercised smart HTTP credentials and SSH signing. The reviewer recorded live SSH remote authentication as unverified. The review also records the limits of independently rehashing Windows binaries from the read-only evidence interface.
