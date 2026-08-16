# MVP 0.1 readiness

Status as of 2026-08-16 (Session B ingestion-hardening milestone, branch
`fable/session-b-ingestion-hardening`). Statuses: `PASS`, `FAIL`, `PARTIAL`,
`NOT RUN`, `BLOCKED`. Real-client results are recorded by a human operator
following [hearthstone-mvp-test-runbook.md](hearthstone-mvp-test-runbook.md)
and are never fabricated.

## Gate A — build: PARTIAL (one Session-A-owned soak assertion pending)

- Debug build and full non-soak test suite: PASS (local, this branch).
- Release build and full non-soak test suite: PASS (local, this branch).
- Soak suites (Debug and Release, local): one known failure — a
  Session-A-owned soak test asserts the pre-F4 match start timestamp; the
  one-line fix is recorded in
  [session-b-ingestion-hardening.md](session-b-ingestion-hardening.md)
  under HANDOFF TO SESSION A. All other soak tests PASS.
- `dotnet format --verify-no-changes` and `git diff --check`: PASS (local).
- GitHub Actions: PASS on `main` (run 31960161760 on `7d31423`); this branch
  has not been pushed, so remote CI and the remote soak job are NOT RUN here.

## Gate B — real client: FAIL (was BLOCKED; a real session ran 2026-08-16)

A human operator ran one real Battlegrounds session against the Debug capture
build (see
[real-client-findings-2026-08-16.md](real-client-findings-2026-08-16.md)).

What worked:

- Scenario B (Hearthstone started before IceCrow): log discovery, tailing,
  parsing, overlay alignment, and click-through all worked.
- `log.config` written by IceCrow was applied by the running client within
  about a minute, without a restart.
- ~61.5 K events were processed live without safety rejections or observer
  detach; capture saves stayed sequential, atomic, and truthful.

What failed:

- The tailer falsely classified the growing log as rewritten mid-match and
  re-read it from byte zero, splitting the real match and duplicating backlog
  captures (F1 — root-caused and fixed offline in this branch; not yet
  re-verified against a live client).
- The client catch-up dump after the live `log.config` change was classified
  as a real match and persisted zero-turn junk captures (F2 — fixed offline
  in this branch; not yet re-verified live).
- Battlegrounds turn/phase never advanced and opponent memory stayed empty on
  real client logs (F6 — owned by Session A, not addressed here).
- Match metadata inherited the stale catch-up timestamp (F4 — fixed offline
  in this branch).
- The overlay rendered raw hero CardIds (F5 — root-caused and fixed offline
  in this branch).

Gate B stays FAIL until a new real-client session verifies the F1/F2/F4/F5
fixes and Session A's F6 reducer semantics against a live match.

## Gate C — evidence: BLOCKED

The committed corpus is synthetic only (`REAL CORPUS PENDING`). The real
capture B from 2026-08-16 cannot be replayed yet (F3 read-preflight limits and
F7 replay work-unit guards, both owned by Session A), and importing
real-anonymized fixtures additionally needs human privacy review. Tooling
(capture, validation, import, anonymization) is complete.

## Gate D — performance: PARTIAL

- No render loop, bounded queues/caches, latest-only overlay dispatch:
  PASS (architecture tests and existing budgets).
- Release capture cost is one null observer check: PASS (composition test).
  Note: the capture overhead baseline diagnostic measures a minimal attached
  observer, not the real Debug `RecordingRuntime` lock path; treat it as a
  lower bound (accurate measurement is an integration follow-up).
- Two-match retained growth in the real client, dotnet-counters profile:
  NOT RUN (needs a Gate B re-run).

## Gate E — known limitations: PASS

Late attach, reconnect truncation, game-type detection, and compatibility
tag drift are documented in the runbook, the acceptance checklist, and
`AGENTS.md`; the capture status model never overstates completeness.

## Ranked remaining blockers

1. F6 — Battlegrounds turn/phase/combat reducer semantics against real client
   logs, plus F3/F7 replay limits (owned by Session A): the core MVP loop and
   all real-evidence replay depend on them.
2. A fresh real-client runbook pass to verify this branch's F1/F2/F4/F5 fixes
   live (Gates B and D).
3. Two reviewed real-anonymized fixtures from genuine captures (Gate C).
