# Real-client findings — 2026-08-31

Live validation session (third real-client session). The operator plays 2–3
real Battlegrounds matches; the agent observes diagnostics, validates every
saved capture through the official pipeline, and records evidence here
continuously. No private identifiers appear in this file; players are
referred to as `LocalPlayer` / `OpponentA` / …, captures as `Capture-1` /
`Capture-2`.

## Session metadata

- IceCrow HEAD: `f4b442d` (final pre-MVP hardening)
- Build: Debug, rebuilt at session start from this HEAD
- Hearthstone mode: Battlegrounds (client started before IceCrow —
  Scenario B shape)
- Games played: 4 complete matches, all captured, no IceCrow restart
  between any of them (14:36–16:01)
- Capture enabled: before the first queue, stayed enabled all session
- Private evidence: local only, never committed
- Pre-existing captures in the store: 3 files from 2026-08-16 (pre-fix
  session; left untouched)

## Pre-session state

Recorded at 14:36, before Game 1 queued (IceCrow launched 14:35 from the
fresh Debug build; Hearthstone had been running since 14:33 — Scenario B):

- Full rereads: 0
- Lifecycle: idle · pending 0 · drops 0 · confirm: - · incomplete: 0
- Incomplete candidates: 0
- Unresolved named refs: 0 (applied-events line clean)
- Capture: Enabled: Yes · Session: Waiting · Persistence: Idle (enabled
  before queueing, per runbook)
- Budget: idle (events 0, retained 0)
- Manacost data: Cache Ready · data version 2026-08-31 10:17:29 · last sync
  2026-08-31 12:35:47 · cards/BG minions/heroes 1327 / 1213 / 121
- Overlay: attached, "Hearthstone connected", 3840x2160
- Power log: configuration ready; 0 raw lines accepted so far (menu)
- Environment note: Hearthstone Deck Tracker is also running (read-only
  co-reader of Power.log; noted as an environment variable, not controlled)

## Game 1

Start: 14:36:59 (confirmed lifecycle) · End: ~14:54 (GameOver at replay
turn 10; capture saved 14:54:17) · Result: placement not recorded

Lifecycle confirmation: exactly one match start, `confirm: StepProgress`
Pending events at confirm: 0 residual (pending 0 at checkpoint)
Candidate drops: 0
Incomplete candidates: 0
Full rereads: 0

Checkpoints (turn · phase · lobby · opponent · applied events · unresolved
refs · capture events · event budget % · retained MiB · retained %):

- Turn 5 · Combat · 9 players · opponent PlayerId 1 · applied 27,731 ·
  unresolved refs 1,016 · capture 27,700 · 11.1% · 8.0 MiB · 8.3%
  (raw/parsed 30,846/28,136 · ignored/unknown/malformed 975/1,734/1)
- Turn 8 · Combat · 9 players · opponent PlayerId 6 · applied 61,913 ·
  unresolved refs 2,165 · capture 61,900 · 24.8% · 17.8 MiB · 18.5%
  (raw/parsed 68,900/62,318 · ignored/unknown/malformed 1,941/4,640/1)
- Turn 10 · Recruit · 9 players · opponent PlayerId 3 · applied 116,980 ·
  unresolved refs 3,963 · capture 116,950 · 46.8% · 33.6 MiB · 35.0%
  (raw/parsed 136,143/117,385 · ignored/unknown/malformed 4,231/14,526/1)

Budget growth note: late-game turns add ~27k events each (t8→t10 = +55k).
Linear projection crosses the 75% warning near turn 13 and 90% near
turn 14–15 — a very long match could exhaust the 250k event budget. The
event budget is the binding constraint; retained bytes grow slower (35.0%
at t10). Watching closely; this is the exact scenario the headroom
diagnostics were added for.

Turns/phases: advancing correctly, Recruit/Combat alternating
Opponent tracking: current opponent tracked (PlayerId visible)
Opponent Memory: verified at replay — 7 opponent histories
Overlay/focus: overlay rendering active (96 views applied), no focus theft
observed
Hero names: not separately verified by the operator this session

Capture (Capture-1):
- Events: 154,610 (RawTagChanged 138,998 · blocks 11,330 · entities 4,277)
- Event budget peak: ~61.8% (154,610 / 250,000) — the F8 recalibration
  held with ~38% headroom on a real full match
- Retained estimate: ~44 MiB projected at end (~46%); 33.6 MiB at t10
- Saved: exactly one file, 36.59 MB — no recorder-limit error, Session
  returned to Waiting, Persistence Saved
- Budget warnings: none (never crossed 75%)

Official validation (Capture-1):
- Load: PASS (deserialized through the official reader)
- Replay: **FAIL — `Replay exceeds the 1000000 event snapshot work-unit
  limit`** (fail-closed guard, InvalidDataException; see finding F10)
- Privacy-safe analyzer: PASS — full event breakdown available; TURN
  stream advances correctly from the confirmation moment (14:37:37)
- Capture preserved unchanged for post-session re-validation

Hero names: overlay rendered hero rows during play; per-name visual
correctness was not separately recorded

Findings: F10 (below) — discovered by this validation, exactly what the
session was designed to catch

## Game 2

Start: 14:55:17 (no IceCrow restart after Game 1). Result: placement not
recorded.

Isolation snapshot at 14:56 (turn 1, Recruit):
- Fresh lobby of 9 players, current opponent PlayerId 7
- Capture counter restarted: 3,500 events / 1.4% · retained 1.0 MiB / 1.0%
  (Game 1 numbers did not leak into Game 2's capture)
- Unresolved named refs reset to a fresh per-match count (91 at t1 vs
  3,963 at Game 1 t10)
- Lifecycle confirmed exactly once again via StepProgress; live candidate
  drops 0 · incomplete 0; cumulative pre-start buffered drops rose to
  5,336 (menu traffic between games — process-lifetime counter, cleared
  from the live candidate by the boundary as designed)
- Full rereads still 0

Checkpoints:

- Turn 7 · Combat · 9 players · opponent PlayerId 4 · capture 58,100 ·
  23.2% · 16.6 MiB · 17.3% · unresolved refs 2,132 · rereads 0 · drops 0
  (session-total applied 212,708)
- Turn 9 · Combat · 9 players · opponent PlayerId 2 · capture 107,150 ·
  42.9% · 30.6 MiB · 31.9% · unresolved refs 3,705 · rereads 0 · drops 0
  (session-total applied 261,791)
- Turn 11 · Recruit · 9 players · opponent PlayerId 4 · capture 155,050 ·
  62.0% · 44.1 MiB · 45.9% · unresolved refs 5,013 · rereads 0 · drops 0
  (session-total applied 309,675)

End: ~15:16 (GameOver at turn 12; capture saved 15:16:35)

Capture (Capture-2):
- Events: 205,922 (RawTagChanged 187,498) — **82.4% of the event budget**,
  the highest real-match usage observed; the final turn alone added ~51k
  events (t11 155,050 → end 205,922)
- File: 48.2 MB · exactly one file · no recorder-limit error · Session
  returned to Waiting · Persistence Saved
- Budget warning: the >75% advisory zone was entered during the final turn
  (between checkpoints); peak usage proves a turn-13+ match of this event
  density would approach the 250k hard cap

Official validation (Capture-2):
- Load: PASS · Replay: **FAIL — same F10 event-snapshot work-unit limit**
- Privacy-safe analyzer: PASS (full breakdown available)
- Capture preserved unchanged

## Cross-match state isolation

- Turn/phase: reset (Game 2 confirmed fresh at turn 1 Recruit, ended
  turn 12 GameOver)
- Lobby: fresh 9-player lobby, distinct current-opponent sequence
- Opponent Memory / timeline: per-match state rebuilt (unresolved-ref
  counter restarted per match: 91 at G2-t1 vs 3,963 at G1-t10)
- Capture stream: Capture-2 counted only Game 2 events (3,500 at t1,
  205,922 final — no Game 1 residue); capture start timestamps match each
  game's own confirmation time (12:36:59Z / 12:55:17Z)
- Entities: no cross-match leakage observed in diagnostics
- Exactly one capture file per game; no duplicate from reread or catch-up
  residue (Full rereads stayed 0 the whole session)
- Between-games menu junk raised the cumulative pre-start drop counter
  (5,336 → 13,097 by Game 2's end) without ever touching a live candidate

## Game 3

Start: 15:17:36 (third consecutive match, still no IceCrow restart).
Result: placement not recorded.

Confirmed once again via StepProgress; fresh capture counter; drops 0,
incomplete 0, Full rereads 0.

Checkpoints:

- Turn 5 · Combat · capture 32,300 · 12.9% · 9.4 MiB · 9.8%
- Turn 8 · Combat · 9 players · opponent PlayerId 8 · capture 82,300 ·
  32.9% · 23.7 MiB · 24.7% · unresolved refs 3,103 · rereads 0 · drops 0
  (session-total applied 442,841 across three matches)
- Turn 10 · Recruit · 9 players · opponent PlayerId 3 · capture 133,450 ·
  53.4% · 38.3 MiB · 39.9% · unresolved refs 4,641 · rereads 0 · drops 0
  (session-total applied 494,007)
- Turn 12 · Recruit · 9 players · opponent PlayerId 8 · capture 190,900 ·
  **76.4%** · 54.6 MiB · 56.9% · unresolved refs 6,299 · rereads 0 —
  **the live `warning: approaching capture budget` advisory displayed**,
  first real-client firing of the headroom warning added this milestone;
  ~28k events/turn in late game, so turn 14 of this density would reach
  the 250k hard cap
- Turn 13 · Recruit · 9 players · opponent PlayerId 5 · capture 213,250 ·
  **85.3%** · 60.8 MiB · 63.4% · unresolved refs 6,843 · rereads 0 —
  advisory warning still displayed; one more full turn (~22-28k events)
  lands at ~94-96%, i.e. the >90% high-risk advisory; two more turns
  would cross the 250k hard cap and discard fail-closed

End: ~15:44 (capture saved 15:44:52); the match ended during turn 13 —
the game concluded almost exactly at the checkpoint values.

Capture (Capture-3):
- Events: **213,253 — peak 85.3% of the event budget**, the closest real
  approach to the 250k cap yet; retained peak 60.8 MiB (63.4%)
- File: 50.45 MB · exactly one file · no recorder-limit error · saved
  cleanly while Game 4 was already queueing
- The `approaching capture budget` advisory displayed live from turn 12
  to the end; the >90% advisory was never reached

Official validation (Capture-3):
- Load: PASS · Replay: **FAIL — same F10 guard** · analyzer PASS ·
  capture preserved

Game 4 note: the operator queued a fourth match immediately (fresh
counter 1,400 events at HeroSelection, 8-player lobby, unresolved refs
reset to 17) — a third consecutive-match isolation data point.

## Game 4

Start: 15:44:51 (fourth consecutive match, still no IceCrow restart).
Result: placement not recorded.

Confirmed once again via StepProgress; fresh counters; rereads 0.

Checkpoints:

- Turn 5 · Combat · 9 players · opponent PlayerId 7 · capture 28,850 ·
  11.5% · 8.3 MiB · 8.7% · unresolved refs 1,273 · drops 0
- Turn 8 · Combat · 9 players · opponent PlayerId 6 · capture 73,600 ·
  29.4% · 21.1 MiB · 22.0% · unresolved refs 2,911 · drops 0
  (session-total applied 647,422 across four matches)
- Turn 10 · Combat · 9 players · opponent PlayerId 1 · capture 130,150 ·
  52.1% · 37.1 MiB · 38.7% · unresolved refs 4,513 · drops 0
  (session-total applied 703,957)

End: ~16:01 (GameOver at turn 10; capture saved 16:01:41)

Capture (Capture-4): 174,684 events (**peak ~69.9%**), 41.17 MB, exactly
one file, no recorder-limit error.

Official validation (Capture-4): Load PASS · Replay (pre-fix): FAIL on
F10; **post-fix: PASSED — replayed 174,684/174,684, turn 10, GameOver**.

## Performance observations

dotnet-counters was intentionally not attached (four back-to-back matches
left no safe idle window; profiling remains a runbook follow-up).
Qualitative observations across ~86 minutes and 966,122 raw lines
(769,331 parsed, 748,461 applied, 6 malformed): the overlay stayed
responsive through four matches (1,061 view updates applied, 284 skipped
by the latest-only policy), no safety rejections, no observer detach, no
visible degradation in the fourth match relative to the first, saves of
36–50 MB completed off the hot path while the next match was already
being tracked.

## Session conclusion

The full internal MVP live chain is proven:

```
4 consecutive real matches
→ tracked (zero rereads, one StepProgress confirmation each)
→ captured (peaks 61.8–85.3% of the event budget, live headroom warning)
→ saved (4 files, one per game, clean isolation)
→ officially loaded (4/4)
→ officially replayed end to end (4/4, after the same-day F10 fix)
→ semantic endings matching the live session exactly
```

F10 was root-caused (honest accounting, undersized budget), recalibrated
from the four-capture corpus, contract-tested at full capacity, and
verified. Open follow-ups: F11 (empty combat-entry board contents in
replay — MEDIUM investigation) and the deferred F9 metadata parser.

## Post-session F10 fix and full revalidation (same day)

The event-snapshot work of all four captures was measured with the new
`analyze-replay-work` FixtureTool command (measurement-only limits):

| Capture | Events | Event-snapshot work | Work/event | Max tags | Replay |
| --- | --- | --- | --- | --- | --- |
| Capture-1 | 154,610 | 1,574,106 | 10.18 | 38 | 1.03 s |
| Capture-2 | 205,922 | 2,065,230 | 10.03 | 39 | 1.50 s |
| Capture-3 | 213,253 | 2,180,808 | 10.23 | 42 | 1.21 s |
| Capture-4 | 174,684 | 1,755,211 | 10.05 | 39 | 1.15 s |

The accounting is honest (each applied event that touches an entity
materializes a FrozenDictionary tag snapshot), so the default budget was
recalibrated to 4,000,000 = 250k events x 16 units/event (measured bound
10.23 x 1.5 safety, rounded up; theoretical writer-acceptable ceiling
~64M). After the fix, **all four captures pass official validation**:

| Capture | Replayed | Turn | Phase | Lobby | Histories | Timeline | Unresolved |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Capture-1 | 154,610/154,610 | 10 | GameOver | 9 | 7 | 186 | 4,916 |
| Capture-2 | 205,922/205,922 | 12 | GameOver | 9 | 7 | 257 | 6,054 |
| Capture-3 | 213,253/213,253 | 13 | GameOver | 9 | 7 | 249 | 6,843 |
| Capture-4 | 174,684/174,684 | 10 | GameOver | 9 | 7 | 225 | 5,347 |

Semantic parity with the live session is exact: final turns match the
live observations (G2 t12, G3 t13, G4 t10), every phase is GameOver, and
the replayed unresolved-reference counts equal the live end-of-match
readings to the digit (6,054 / 6,843 / 5,347).

## Event volume and the 250k budget

- Composition (Captures 1–2 breakdowns): ~90% `RawTagChanged`, ~5.5%
  block markers, ~3-4% entity declarations/reveals — normal client
  verbosity for Battlegrounds; no evidence of duplicate normalized events
  or multiple log sources, so no deduplication is warranted.
- Peaks: 61.8% / 82.4% / 85.3% / 69.9% of the 250k event budget.
- Decision: **KEEP 250k for the internal MVP.** All four real matches fit
  with >=14.7% headroom, the live >75% advisory gives the operator
  visibility, and raising the cap would force retained/file-cap and
  retention re-analysis without a single observed overflow. Revisit only
  if a real match is ever discarded at the cap.

## New findings

### F10 — replay event-snapshot work guard not calibrated for full matches

- Status: **FIXED same day and verified against all four captures**
  (measurement, recalibration to 4M, full-capacity contract tests, four
  official validation passes; see the tables above).
- Severity: HIGH
- Evidence: Capture-1 (154,610 events, writer-accepted at 61.8% of the
  event budget) loads through the official reader but the official replay
  fails closed with `Replay exceeds the 1000000 event snapshot work-unit
  limit`. The 2026-08-16 half-match capture (61,543 events) replayed under
  the same guard; a full match does not.
- Impact: blocks the Gate B chain (capture saved → load → **replay**) for
  every real full match; live tracking and the save path are unaffected.
- Owner: `IceCrow.Recording` (`ReplayRunner.MaximumEventSnapshotWorkUnits`,
  charged as 1 + entity tag count per applied-event snapshot in
  `ReserveEventSnapshotWork`).
- Root-cause shape: F7 recalibrated the *timeline* work guard against real
  matches, but the *event snapshot* guard kept its synthetic 1M constant —
  the write path accepts 250k events while the replay guard saturates near
  ~150k real events.
- Suggested fix: measure the actual work units the real capture charges,
  then recalibrate the constant with the same evidence-first method as F7
  (scale with MaximumEventCount, keep linearity, keep hostile-input
  fail-fast), plus a full-capacity replay contract test mirroring the
  write-side flood test.
- Regression test: replay a MaximumEventCount-scale tag-flood recording
  through ReplayRunner defaults.
- MVP blocker: was YES; resolved.

### F11 — combat-entry opponent board snapshots replayed with zero minion work

- Severity: MEDIUM (investigation)
- Evidence: all four replays report `board snapshot work: 0` even though
  every replay populated 7 opponent histories. The work counter charges
  1 + tags per minion in each observed board, so 0 total means every
  combat-entry board snapshot contained no minions. The 2026-08-16 real
  capture replayed with minion-bearing boards under the same engine.
- Impact: opponent histories exist (presence/turn data intact) but the
  remembered board contents may be empty for the current client's combat
  entry ordering — the overlay would show "board empty" instead of the
  opponent's minions. Not a Gate B chain blocker (capture, load, replay,
  and semantic endings are all correct), but it degrades the product's
  core opponent-memory value if confirmed live.
- Owner: `IceCrow.Battlegrounds`/`IceCrow.Tracking` combat-entry snapshot
  timing (zone/controller tag ordering at the combat transition).
- Suggested next step: inspect one capture's combat-entry window with the
  privacy-safe analyzer (zone/controller tag sequences around
  `NEXT_OPPONENT`/step transitions), then adjust the snapshot trigger or
  confirm the client now populates boards after the trigger moment; add a
  real-anonymized fixture checkpoint for a non-empty remembered board.
- MVP blocker: NO (flagged for the next milestone; verify visually in the
  next live session).
