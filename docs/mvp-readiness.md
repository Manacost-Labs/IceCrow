# MVP 0.1 readiness

Status as of 2026-08-17, integrated `main` after the second live session and
the history rewrite (all pre-rewrite SHAs are labeled historical; current
references are reachable commits only). Historical session detail lives in
[session-a-real-semantics-recovery.md](session-a-real-semantics-recovery.md)
and [session-b-ingestion-hardening.md](session-b-ingestion-hardening.md).
Statuses: `PASS`, `FAIL`, `PARTIAL`, `NOT RUN`, `BLOCKED`. Real-client
results come from a human operator following the
[runbook](hearthstone-mvp-test-runbook.md) and are never fabricated.

**Overall: INTERNAL MVP CODE READY — ONE LIVE EVIDENCE PIECE REMAINING
(a saved full-match capture).**

## Real-client findings status (after the 2026-08-17 session)

- F1 tailer false re-read — **live verified** (second session: zero full
  rereads across ~295k processed lines).
- F2 catch-up false matches — **live verified** (the startup catch-up dump
  armed but never confirmed; the match confirmed exactly once via semantic
  `StepProgress`, and post-match junk produced the fail-closed warning, not
  a false start).
- F3 recording read-preflight — fixed; the real capture loads through the
  official `RecordingSerializer.LoadAsync`.
- F4 stale match timestamps — fixed offline; captures start at the
  confirming evidence time.
- F5 hero CardId fallback — fixed offline (card metadata + skin
  normalization); live re-verification pending.
- F6 turn/phase/board semantics — **live verified** (turns, Recruit/Combat
  transitions, current opponent, and Opponent Memory captures all advanced
  correctly during the second session).
- F7 replay work accounting — fixed; timeline work now charges actual
  mutations (inserts and evictions), stays linear, and the real capture
  replays under default limits (~0.6–1.1 s for 61,543 events).
- F8 recorder budget too small for a real full match — **live verified**
  (2026-08-31 session: four consecutive full matches saved at 154k / 206k /
  213k / 175k events, peak 85.3% of the 250k budget; the >75% headroom
  advisory fired live exactly as designed; no recorder-limit discard).
- F10 replay event-snapshot work guard not calibrated for full matches —
  **NEW (2026-08-31, HIGH)**: every saved full-match capture loads but
  fails official replay on `MaximumEventSnapshotWorkUnits = 1M`; see
  [real-client-findings-2026-08-31.md](real-client-findings-2026-08-31.md).
  Fixed-by: pending (the F7 evidence-first recalibration method applies).

## Gate A — build: PASS (current-HEAD remote soak pending)

- Debug and Release builds and full non-soak suites: PASS (local, this HEAD).
- Local soak, Debug and Release: PASS (this HEAD).
- `dotnet format --verify-no-changes` and `git diff --check`: PASS (local);
  CI now also proves the suite leaves the worktree clean, and the whitespace
  gate is deterministic (whole-tree check, immune to force-push history).
- Remote quality CI: PASS on `80770b3` (dispatch run 31977992196). The push
  run on the same SHA failed only because the old whitespace step diffed
  against a base revision the history rewrite had removed — root-caused and
  fixed in this milestone.
- Remote long-run soak: PASS on a pre-rewrite build (the exact commit id is
  intentionally not reproduced — see
  [privacy-history-decision.md](privacy-history-decision.md)). The current
  HEAD has no matching remote soak yet — local soak covers it; dispatching
  `run_soak = true` needs owner authorization.

## Gate B — real client: PARTIAL (major progress in session two)

- Offline replay of the 2026-08-16 real evidence through the official
  pipeline: PASS (load, replay, turns, phases, opponent boards).
- Second live session (2026-08-17,
  [real-client-findings-2026-08-17.md](real-client-findings-2026-08-17.md)):
  semantic lifecycle confirmation (`StepProgress`), boundary reset of
  pre-start junk, fail-closed warning semantics, full-match live tracking
  with opponent memory, and enable-mid-match capture semantics all
  verified PASS against a real ~25-minute match; no false re-reads, no
  false matches, no safety rejections.
- Third live session (2026-08-31,
  [real-client-findings-2026-08-31.md](real-client-findings-2026-08-31.md)):
  **four consecutive full matches tracked, captured, and saved with clean
  cross-match isolation and zero rereads** — the save leg of Gate B is
  live-verified at real scale (peak 85.3% event budget).
- Remaining live gap: the **replay** leg. All four saved captures load
  through the official reader but fail replay on the uncalibrated F10
  event-snapshot work guard. Gate B closes by fixing F10 offline and
  re-validating the four preserved captures — no new live session needed.

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
confidence are never overstated. The Git-history rewrite (Option B) was
executed on 2026-08-17 and is recorded — including what it does not
guarantee — in [privacy-history-decision.md](privacy-history-decision.md).

## Ranked remaining blockers (updated 2026-08-31)

1. F10 — recalibrate `ReplayRunner.MaximumEventSnapshotWorkUnits` against
   the four preserved real captures (F7 method) with a full-capacity replay
   contract test, then re-run `validate-latest-private-capture.ps1` on all
   four; this alone closes the replay leg of Gate B.
2. Live performance profile with dotnet-counters (Gate D) — the only
   runbook item the 2026-08-31 session did not cover.
3. Human privacy review and `APPROVED FOR COMMIT` for the fixture candidate
   (Gate C), then a second reviewed fixture.
