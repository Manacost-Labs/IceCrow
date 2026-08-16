# MVP 0.1 readiness

Status as of 2026-08-16, integrated `main` (all Session A and Session B work
merged; branch-ownership language retired — historical detail lives in
[session-a-real-semantics-recovery.md](session-a-real-semantics-recovery.md)
and [session-b-ingestion-hardening.md](session-b-ingestion-hardening.md)).
Statuses: `PASS`, `FAIL`, `PARTIAL`, `NOT RUN`, `BLOCKED`. Real-client
results come from a human operator following the
[runbook](hearthstone-mvp-test-runbook.md) and are never fabricated.

**Overall: INTERNAL MVP CODE READY — EVIDENCE GATE PARTIAL.**

## Real-client findings status (2026-08-16 session)

- F1 tailer false re-read — fixed offline; live re-verification pending.
- F2 catch-up false matches — fixed offline; confirmation now additionally
  requires later *semantic* Battlegrounds progression (a Battlegrounds tag,
  positive TURN, setup/combat tag, or mulligan/main step), so an arbitrary
  later event can no longer confirm; live re-verification pending.
- F3 recording read-preflight — fixed; the real capture loads through the
  official `RecordingSerializer.LoadAsync`.
- F4 stale match timestamps — fixed offline; captures start at the
  confirming evidence time.
- F5 hero CardId fallback — fixed offline (card metadata + skin
  normalization); live re-verification pending.
- F6 turn/phase/board semantics — fixed; the real capture replays to
  turn 7 with recruit/combat transitions and seven remembered opponent
  boards; unresolved named references are counted and surfaced in live
  diagnostics.
- F7 replay work accounting — fixed; timeline work now charges actual
  mutations (inserts and evictions), stays linear, and the real capture
  replays under default limits (~0.6–1.1 s for 61,543 events).

## Gate A — build: PARTIAL (remote runs pending push authorization)

- Debug and Release builds and full non-soak suites: PASS (local, this HEAD).
- Local soak, Debug and Release: PASS (this HEAD).
- `dotnet format --verify-no-changes` and `git diff --check`: PASS (local).
- Remote quality CI: PASS on `bbc934c` (run 31965300963); the evidence-closure
  commits after it are not pushed, so remote CI and the remote soak job for
  this HEAD are NOT RUN. Owner action: authorize push, then dispatch the CI
  workflow with `run_soak = true` and record the run id.

## Gate B — real client: PARTIAL

- Offline replay of the 2026-08-16 real evidence through the official
  pipeline: PASS (load, replay, turns, phases, opponent boards).
- First live session results (what worked and what failed) are recorded in
  [real-client-findings-2026-08-16.md](real-client-findings-2026-08-16.md);
  every failure has an integrated offline fix with regression tests.
- Fresh live verification of the integrated fixes (one complete match, two
  consecutive matches, catch-up/late start, API offline): NOT RUN — needs a
  human operator with the Hearthstone client running the runbook.

## Gate C — evidence: PARTIAL

- Candidate `real-solo-turn-phase-board-001` (7,181-event exact slice, four
  checkpoints, anonymizer passed, fresh golden validation green, automated
  scans clean) sits outside the repository with a machine-generated review
  package; the reviewer path is
  [real-fixture-privacy-review-checklist.md](real-fixture-privacy-review-checklist.md).
- Status: REAL FIXTURE CANDIDATE READY · HUMAN PRIVACY REVIEW REQUIRED ·
  NOT COMMITTED. The corpus remains synthetic-only until the owner records
  `APPROVED FOR COMMIT`. Target remains two reviewed real fixtures.

## Gate D — performance: PARTIAL

- No render loop, bounded queues/caches/counters, latest-only overlay
  dispatch: PASS (architecture tests and budgets).
- Release capture path is a null observer: PASS (composition guard test);
  the capture overhead baseline measures a minimal attached observer and is
  a lower bound for the Debug runtime path.
- Real capture replay throughput: 54k–104k events/s offline.
- Live CPU/memory profile (dotnet-counters across menu/recruit/combat/save,
  two matches): NOT RUN — belongs to the Gate B session.

## Gate E — known limitations: PASS

Late attach, reconnect truncation, game-type detection, compatibility-tag
drift, and the uncertainty policies remain documented in the runbook, the
acceptance checklist, and `AGENTS.md`; capture completeness and identity
confidence are never overstated. The Git-history privacy decision is
recorded as pending in
[privacy-history-decision.md](privacy-history-decision.md).

## Ranked remaining blockers

1. Second real-client runbook session (Gates B and D): one complete match,
   two consecutive matches, catch-up/late-start, API-offline, with
   dotnet-counters evidence.
2. Human privacy review and `APPROVED FOR COMMIT` for the fixture candidate
   (Gate C), then a second reviewed fixture.
3. Owner-authorized push plus a remote CI + soak dispatch for this HEAD
   (Gate A).
