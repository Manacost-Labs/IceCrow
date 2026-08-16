# Session B — Power.log ingestion, lifecycle, and MVP usability hardening

Parallel engineering session report. Session B owns live-ingestion
reliability and MVP usability (F1, F2, F4, F5, privacy/docs). Session A
concurrently owns F3/F6/F7 (Recording, Battlegrounds reducers, replay
guards). All information below is sanitized; no private identifiers or
local paths.

## BASE COMMIT

`4f125cf8287cca3bc1dd298f29bec7d54bc7c72b`, branch
`fable/session-b-ingestion-hardening`, worktree `../IceCrow-session-b`.

## PRIVACY CLEANUP

- `docs/real-client-findings-2026-08-16.md`: the local BattleTag →
  `LocalPlayer`, the opponent nickname → `FinalOpponent`, the local
  installation path → `<Hearthstone installation>`, private capture filename
  fragments → stable labels `A`/`B`/`C`.
- Repository scan for BattleTag-like strings found no other real
  identifiers (the only other matches are the synthetic `SecretHero#1234`
  anonymizer test values).
- New focused guard in `tests/IceCrow.Architecture.Tests/
  PrivateCaptureGuardTests.cs`: scans `docs/real-client-*.md` and
  `tests/fixtures/battlegrounds/**` for BattleTag-like identifiers and
  drive-rooted local installation paths; reports only path/line/category,
  never the matched value.

## GIT HISTORY NOTICE

HISTORY CONTAINS PRIOR IDENTIFIER — the pre-session commits (`4f125cf`,
`7d31423`) still contain the original identifiers in the findings report.
A follow-up commit cannot remove them.
HISTORY REWRITE DECISION REQUIRED BY REPOSITORY OWNER. No history was
rewritten in this session.

## F1 EXACT ROOT CAUSE

`PowerLogTailer.ReadFileAsync` mixed a `FileInfo` directory-metadata
snapshot with live handle state:

1. The same-length rewrite check armed on `file.Length == ByteOffset ==
   ObservedLength` taken from the `FileInfo` snapshot. When the caught-up
   tailer opened the stream after Hearthstone appended between that snapshot
   and `stream.Length` capture (a窗口 of well under a millisecond, exercised
   thousands of times per session by watcher signals), the check armed while
   the file had really grown.
2. The read loop then consumed the new bytes and advanced the offset, and
   the post-read comparison hashed the 64 KiB window ending at the NEW
   offset against the armed fingerprint of the window ending at the OLD
   offset. Different windows → guaranteed mismatch → full reset to byte
   zero, re-emitting the catch-up `CREATE_GAME` (splitting the live match
   and duplicating the backlog exactly as observed at 18:47:08).

Secondary latent defect fixed by the same change: `file.Length <
ByteOffset` (shrink detection) used directory metadata, which can lag the
true end-of-file for a file held open by another process; a stale value
would be misclassified as truncation. The reproduced-and-tested mechanism
is (1); (2) is proven unsound by inspection and removed with it.

## F1 RESET REASON

Under the old enum the false reread records as `ContentChangedDuringRead`
(the armed same-length comparison across two different windows). No reset
reason existed before this session; diagnostics were added first so any
future occurrence is attributable.

## F1 ALGORITHM BEFORE

Reset to byte zero when any of: path changed; creation time changed; stale
`FileInfo` length < consumed offset; stored 4 KiB prefix fingerprint
mismatch; same-length content fingerprint mismatch at open (armed from
stale metadata); prefix changed during read; content fingerprint of the
(possibly advanced) end-of-file window changed during read.

## F1 ALGORITHM AFTER

`LogCheckpointContinuity.VerifyAsync` — all decisions from the open handle,
bounded to 4 KiB + 64 KiB reads:

1. Path differs → reset (`PathChanged`).
2. `stream.Length < ByteOffset` → reset (`FileShrank`).
3. Stored prefix fingerprint (first ≤4 KiB) mismatch → reset
   (`PrefixChangedBeforeRead`).
4. The 64 KiB window ending exactly at the consumed offset no longer hashes
   to the checkpoint window fingerprint → reset (`CheckpointWindowChanged`).
5. Otherwise the file is a proven append-only continuation — never reset,
   regardless of timestamps, creation-time changes, or directory metadata.

During-read verification is pinned to the pre-read state: the opened prefix
and the window ending at the STARTING offset are re-verified after the
read (`PrefixChangedDuringRead` / `ContentChangedDuringRead`). Creation
time is no longer a reset trigger by itself (a provably continuous file
continues). Partial-line semantics are unchanged: the offset only advances
on complete lines, so a reopened checkpoint always ends on a line boundary.

Known accepted limitation: a replacement file that keeps the same path,
first 4 KiB, total length ≥ offset, and the exact 64 KiB before the offset
is treated as continuous; with an append-only game log this requires a
deliberately crafted file.

## FULL REREAD DIAGNOSTICS

`PowerLogTailerDiagnostics` (bounded, no paths): `FullRereadCount` (same-
path resets that discarded a non-zero offset), `LastResetReason`,
`LastResetAt`, `LastResetObservedLength`, `LastResetOffset`. Exposed via
`LiveRuntime` → `IceCrowRuntime` → developer diagnostics presenter (pulled
on the existing 250 ms batch tick, no new timers) → `Full rereads: N ·
Last reset: …` line in the Debug window.

## F2 CATCH-UP ROOT CAUSE

The lifecycle detector started an authoritative Battlegrounds match on a
single weak tag (`PLAYER_TECH_LEVEL` / `PLAYER_TRIPLES` /
`NEXT_OPPONENT_PLAYER_ID`). The client's catch-up dump after a live
`log.config` change replays the current UI context as exactly that shape —
`CREATE_GAME`, GameEntity, two players, and a Battlegrounds-like tag burst
all sharing one log timestamp with no progression — so the menu snapshot
became a persisted zero-turn "match".

## F2 CONFIRMATION POLICY

Candidate vs confirmed, detector remains the single lifecycle authority:

- A Battlegrounds tag only ARMS candidate evidence.
- A later-timestamped subsequent event CONFIRMS the match (real matches
  advance log time immediately; the real capture-A burst never does — all
  260 events share one timestamp).
- `STATE=COMPLETE` inside an unconfirmed burst clears armed evidence as
  snapshot residue of an already-finished game.
- The coordinator is unchanged: events buffer in the existing bounded
  pending queue while unconfirmed and replay exactly once on the single
  confirmed start. Confirmation typically arrives one event after the
  evidence in a real match, so the buffered window stays far below the 512
  default capacity (the real dump was 260 events).

## F4 TIMESTAMP POLICY

`BattlegroundsLifecycleObservation` now carries both `MatchStartedAt`
(candidate boundary time) and `ConfirmedAt` (timestamp of the confirming
event). The coordinator starts tracking and notifies the capture observer
with the confirmation time. No arbitrary age threshold is introduced: the
confirmation timestamp is evidence by construction, stays within
milliseconds of the boundary for a normal match, and replaces the stale
18:38:22 catch-up time with real match time for a catch-up session.
Buffered pre-start events keep their genuine earlier timestamps; no
serialized format changed.

## F5 ROOT CAUSE

Two independent defects, both proven from code:

1. `BattlegroundsOverlayViewStateFactory` never consulted `ICardDatabase`
   for hero rows (minions did), so raw CardIds rendered even with a healthy
   Manacost cache.
2. The Manacost heroes dataset keys heroes by base card id, while the
   client reports cosmetic skin ids (`BG22_HERO_000_SKIN_E`), so a hero
   lookup would still have missed skins.

`Player 12` rows come from lobby players with no hero entity data at all
(catch-up/lifecycle context, addressed by F2/F6).

## HERO LOOKUP RESULT

- `InMemoryCardDatabase.GetHeroByCardId`: exact id first, then the
  documented `_SKIN_<variant>` suffix normalization
  (`BattlegroundsHeroSkins`, uppercase/digit suffix only, O(1), no network,
  unknown stays unknown).
- Presentation resolves hero names metadata-first, then the log-provided
  entity name. A raw CardId is never shown as a name: with a card id but no
  resolution the row reads `Unknown hero` (raw id remains on
  `OpponentOverlayViewState.HeroCardId` for tooltips/diagnostics), and
  `Player N` when no hero is known at all.

## MANACOST CACHE RESULT

The 2026-08-16 session's raw-CardId symptom is fully explained by the two
code defects above, which reproduce with ANY cache state. The Debug window
already surfaces `CacheReady`, card/hero counts, last sync time, and the
sync error string; no additional lookup-miss counters were added (the exact
lookup path is now deterministic and unit-tested). Offline-first behavior,
no tokens, no per-card HTTP, and bounded retries are unchanged.

## TESTS

- `LogCheckpointContinuityTests` (unit): append growth, stale-metadata
  states, the armed same-length race shape, creation-time mismatch,
  truncation, same-length window rewrite, prefix rewrite, path switch.
- `PowerLogTailerContinuityTests` (integration): creation-time change with
  append-only growth (no reread, no duplicates), burst/idle-gap write
  pattern with exactly-once in-order delivery, single CREATE_GAME, zero
  full rereads.
- `PowerLogTailerDiagnosticsTests`: append-only records nothing; a
  truncating rewrite records exactly one reread with reason and offset;
  directory rotation is recorded but not counted as a reread.
- `BattlegroundsLifecycleConfirmationTests`: sanitized catch-up burst (no
  start, no false end, no observer notification, with and without
  `STATE=COMPLETE` residue), snapshot-then-live continuation confirming
  once with snapshot identities, detector-level exactly-once confirmation,
  F4 start-time assertions (confirmation time, monotonic starts).
- Data/Presentation: skin normalization pattern boundaries, skin→base hero
  lookup, metadata-first hero names, Unknown-hero fallback policy.
- Existing live tests updated to feed advancing log timestamps (as the real
  client does); the raw lifecycle fixture checkpoint moved from the
  evidence line to the confirming line.

Final gate on this branch (local): Debug build + full non-soak suite PASS,
Release build + full non-soak suite PASS, `dotnet format
--verify-no-changes` PASS, `git diff --check` clean. Soak suites: see
LOCAL SOAK.

## LOCAL SOAK

Debug and Release soak suites were executed locally on this branch. All
soak tests PASS except ONE known failure in a Session-A-owned file:
`IceCrow.FixtureTool.Tests.LiveRecordingSoakTests.
ManyCapturedConsecutiveMatchesStayBoundedAndIndependent` asserts the
recorded `StartedAt` equals the evidence-line time, while under the F4
policy the confirmed start is the confirming event's time (5 ms later in
that fixture). The exact one-line fix is recorded under HANDOFF TO
SESSION A; Session B did not edit the Session-A-owned test. Remote
workflow soak: NOT RUN (branch not pushed; pushing/dispatching requires
explicit user instruction).

## SECURITY

Reviewed the changed surface (see the final session summary for the diff
review): all tailer length decisions moved to the open handle (stale
directory metadata can no longer influence resets); fingerprint reads stay
bounded (4 KiB + 64 KiB windows); no new allocations proportional to file
size; diagnostics contain no paths or log content; the privacy guard
reports category/location only; hero lookup adds no network, no unbounded
work, and no fabricated data; no new packages, projects, or dependency
edges. No token or credential surface touched.

## COMMITS

1. `84b286d` — Sanitize real-client reports and add privacy guards
2. `400552e` — Add explicit Power.log reset diagnostics
3. `2bfc6aa` — Harden Power.log rewrite detection with proven append
   continuity
4. `2bda764` — Surface tailer reset diagnostics in the developer window
5. `13c027d` — Reject catch-up snapshots as real Battlegrounds matches
6. `ad30f30` — Use confirmation evidence time for verified match start
7. `39b981f` — Resolve hero names through card metadata with skin
   normalization
8. (docs) — Update MVP readiness, runbook, and this report

## HANDOFF TO SESSION A

- REQUIRED one-line test fix in
  `tests/IceCrow.FixtureTool.Tests/LiveRecordingSoakTests.cs` line 51:
  the expected recorded start is now the confirming event's timestamp —
  `Timestamp.AddMilliseconds(offset)` must become
  `Timestamp.AddMilliseconds(offset + 5)` (the `NEXT_OPPONENT_PLAYER_ID`
  line at index 5 confirms the match armed by the tech-level line at
  index 4). This is the only failing test on the Session B branch.
- `tests/fixtures/battlegrounds/synthetic-raw-lifecycle/expected.json`: the
  `BattlegroundsDetected` checkpoint moved from `eventIndex: 4` to `5`
  because a match now confirms on the first later-timestamped event after
  the evidence line. Any new fixtures should place lifecycle-start
  checkpoints on the confirming line, not the evidence line.
- Live tests and the golden runner feed advancing per-line timestamps; a
  fixture whose events all share one timestamp will (by design) never
  confirm a match.
- Match start timestamps now use the confirmation event time
  (`BattlegroundsLifecycleObservation.ConfirmedAt`). If recordings should
  additionally persist the candidate boundary time, that is a Session A
  format decision; Session B did not change any serialized format.
- Capture-side guard suggested by the findings (discard a completed capture
  that never reached turn 1) was NOT implemented — Recording is Session A
  territory; with F2 the lifecycle no longer starts such matches, so the
  guard is defense-in-depth if still wanted.

## HANDOFF TO INTEGRATOR

- The `attached-idle` capture overhead diagnostic
  (`tests/IceCrow.FixtureTool.Tests/CaptureOverheadBaselineTests.cs`,
  Session A ownership) measures a minimal attached observer, not the real
  Debug `RecordingRuntime` lock path. `docs/mvp-readiness.md` Gate D now
  describes it as a lower bound; after branch integration consider a real
  Debug-runtime measurement.
- After integrating Sessions A and B, run a fresh real-client runbook pass
  to verify F1/F2/F4/F5 live and re-evaluate Gate B; then push and dispatch
  the remote soak workflow (`workflow_dispatch`, `run_soak = true`).
- Prior Git history still contains private identifiers (see GIT HISTORY
  NOTICE); a history rewrite, if desired, must be coordinated by the
  repository owner across both session branches.
