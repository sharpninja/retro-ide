# Phase 04 verification: emulator manager and adapter conformance

Phase 04 passed independent hostile validation on November 27, 2026. GPT-6.2-SOL High reviewed the frozen `1bc88c38b9444feed29734c68e31a01279269589` candidate on LEGION2. The gate requires at least 98 in each dimension for every judged scope.

| Scope | Accuracy | Completeness |
| --- | ---: | ---: |
| 04A: Packages and pinned versions | 99 | 98 |
| 04B: Sessions and adapter conformance | 99 | 98 |
| 04C: Machine descriptors and selected package profiles | 99 | 98 |
| Reopened MCP package parity (`EXEC-TODO-003`) | 99 | 99 |
| Phase 04 delivery (`EXEC-TODO-011`) | 99 | 98 |
| Overall | 99 | 98 |

[Independent verdict and criterion matrix](hv/20261127T190752-phase-04-accepted-review.verdict.md) · [Frozen source](https://github.com/sharpninja/retro-ide/commit/1bc88c38b9444feed29734c68e31a01279269589)

## Verified behavior

- The Emulators pane and scoped MCP tools share the Git bucket catalog, transactional installation, verified rollback, and pinned binding resolver. An older project read cannot replace the current selection after a project switch. Overlapping package notifications during local work coalesce into a follow-up read.
- The adapter fixture exercises launch and attach, identity and capability negotiation, ordered events and gap recovery, uncertain mutation reconciliation, bounded media, and attachment cleanup. Control and media resources close before the adapter connection and bundle lease are released, including when consumers pause between items.
- The selected package supplies a verified debugger profile. The adapter descriptor identifies the active machine and CPUs, address spaces, memory regions and maps, timing, and byte-order defaults. Reads and writes recheck observed state, capabilities, and mapping revisions before routing. Independently advertised raw inspection remains available when a detailed descriptor is unavailable.

LAB-OMARCHY and LEGION2 each passed 394 managed cases with zero failures or skips and passed input-free native RemoteControl. LEGION2 passed a separate 110-case focused emulator run using 238 hash-verified files built on Linux. Linux also passed isolated X11 docking and effective Wait-cursor checks. The trace gate confirmed 273 links, 170 public contracts, and 42 canonical criteria.

## Remaining phase work

Phase 05 covers production UAE, VICE, and ViceSharp lifecycle, offline launch, reattachment, and shipped capability receipts. Phase 06 covers debugger presentation, the selected target's live views, and the persistent endianness preference. The accepted fixture and package results define the contract used by those phases. The review records the retained test-first chronology and the limit of the source-traced subscription-open schedule.

[Emulator adapter contract](../emulator-adapter-contract.md) · [Implementation plan](../plans/PLAN-RETROIDE-001.md) · [Versioned C4 model](../architecture/structurizr/README.md)
