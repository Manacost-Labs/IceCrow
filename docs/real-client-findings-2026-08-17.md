# Real-client findings — 2026-08-17 (second session)

Second live Hearthstone Battlegrounds session against the Debug build at
`d5b7191` (pre-live hardening v2), session log
`Logs\Hearthstone_2026_08_17_00_00_26`. One match was conceded early with
capture disabled; one full match (~25 minutes, second place) ran with
capture enabled. No private identifiers appear in this report.

## What the hardened pipeline proved live

- **Semantic confirmation worked exactly as designed.** The client again
  emitted a menu-context `CREATE_GAME` power dump at startup — this time
  with *advancing* timestamps, the precise shape the v2 hardening
  anticipated. Metadata armed the candidate but never confirmed; the match
  confirmed once via `StepProgress`, and the developer window showed
  `confirm: StepProgress` with `drops 0 · incomplete: 0` for the live
  candidate.
- **Boundary reset absorbed the between-games junk stream.** The cumulative
  pre-start buffer drops (~5.6k from menu traffic between the startup dump
  and the first match boundary) were cleared by `CREATE_GAME` before
  confirmation, so the fail-closed guard never had to fire against a real
  match — and after the match ended, the armed-again candidate correctly
  showed the `pre-start evidence dropped; waiting for next game` warning
  instead of a false start.
- **Live tracking was correct end to end**: turns and Recruit/Combat phases
  advanced, a 9-player lobby with the current opponent tracked, opponent
  boards were captured into Opponent Memory on combat entry, and the
  overlay stayed aligned without stealing focus. ~295k raw lines were
  processed with 1 malformed line and no safety rejections.
- **Enable-mid-match semantics held**: capture enabled during the first
  (conceded) match correctly waited for the next match instead of
  producing a partial recording.

## F8 — the recorder event budget was too small for a real full match

**Severity: high (blocked Gate B capture evidence); fixed same-session.**

The full match reached ~44k applied capture events by turn 6 and exceeded
the 100,000-event recorder cap before its natural end. `MatchCaptureSession`
discarded the capture honestly at match end — the developer window showed
`Error: Recorder limit reached…`, no file appeared, and nothing false was
persisted ("fail loudly, not falsely" held exactly).

Fix (commit `e340063`, evidence-calibrated): 250,000 events, 96 MiB
retained estimate, 128 MiB file cap, read preflight still derived as
8x retained; the hostile-flood contract test scales to the new ceiling.
The new limits carry ~2x headroom above the measured full-match
projection but are **not yet verified by a completed live capture** — the
first successfully saved full-match capture is the next session's goal.

## F9 — `GameState.DebugPrintGame()` lines carry authoritative match metadata

**Severity: opportunity (removes two known limitations).**

The unknown-line counter (~32k) led to `GameState.DebugPrintGame()` lines
containing `GameType=GT_BATTLEGROUNDS`, `BuildNumber`, `ScenarioID`, and an
authoritative `PlayerID → PlayerName` map. Parsing this block would:

1. give exact game-type classification from Power.log alone (currently
   listed as needing client-state support), and
2. seed the entity-name resolver with proven player-name associations,
   cutting the unresolved-named-reference count (which reached ~1.8k
   during combats, dominated by intentionally-ambiguous duplicate minion
   names but including player-name references).

Owner: `Hearthstone.Protocol` (new accepted line kind) + `Live` lifecycle /
`Entities` associations. Not implemented mid-session.

## Mechanics coverage confirmed on real data

Trinkets (~1.9k tag lines), excavate tags, and all shop/combat actions
flow through the parser as normalized `TAG_CHANGE` events into the entity
store and the capture stream verbatim — nothing is lost even where the
Battlegrounds reducer does not yet model the mechanic semantically.
Opponent-board memory is semantic already (per-combat snapshots with
bounded history).

## Session outcomes against the runbook

- Pre-live verification: PASS (rereads 0, lifecycle idle, capture off at
  start).
- One complete match, tracking/overlay/diagnostics: PASS.
- One complete match **capture saved**: FAIL → root-caused (F8) → fixed →
  re-verification pending next session.
- Concede path: PASS (no capture for a disabled-capture match; no false
  lifecycle end).
- Two consecutive captured matches: NOT RUN (session ended before a
  post-fix match).
- dotnet-counters profile: NOT RUN.

## Next session goal

One post-fix match with capture enabled from the start: expect a single
saved capture (~50–100 MB), `validate-latest-private-capture.ps1` green,
and replay turn/phase matching the observed game — that closes the last
live piece of Gate B and yields the first full-match fixture candidate.
