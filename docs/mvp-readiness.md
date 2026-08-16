# MVP 0.1 readiness

Status as of 2026-08-16 (capture-hardening milestone). Statuses: `PASS`,
`FAIL`, `PARTIAL`, `NOT RUN`, `BLOCKED`. Real-client results are recorded by
a human operator following
[hearthstone-mvp-test-runbook.md](hearthstone-mvp-test-runbook.md) and are
never fabricated.

## Gate A — build: PARTIAL

- Debug build and full test suite: PASS (local).
- Release build and full test suite: PASS (local).
- `dotnet format --verify-no-changes` and `git diff --check`: PASS (local).
- GitHub Actions on these commits: NOT RUN (commits not pushed yet; the
  previous pushed HEAD `ab14b19` was green including the manual soak job).

## Gate B — real client: BLOCKED

No human operator with a live Hearthstone client has executed the runbook
yet. All Scenario A–H items are NOT RUN. Blocked on a real-client session.

## Gate C — evidence: BLOCKED

The committed corpus is synthetic only (`REAL CORPUS PENDING`). Importing
real-anonymized fixtures is blocked on Gate B captures plus human privacy
review. Tooling (capture, validation, import, anonymization) is complete.

## Gate D — performance: PARTIAL

- No render loop, bounded queues/caches, latest-only overlay dispatch:
  PASS (architecture tests and existing budgets).
- Release capture cost is one null observer check: PASS (composition test
  plus the capture overhead baseline diagnostic).
- Two-match retained growth in the real client, dotnet-counters profile:
  NOT RUN (needs Gate B session).

## Gate E — known limitations: PASS

Late attach, reconnect truncation, game-type detection, and compatibility
tag drift are documented in the runbook, the acceptance checklist, and
`AGENTS.md`; the capture status model never overstates completeness.

## Ranked remaining blockers

1. A real Hearthstone session executing the runbook (Gates B and D).
2. Two reviewed real-anonymized fixtures from genuine captures (Gate C).
3. Push + green GitHub Actions run for the hardening commits (Gate A).
