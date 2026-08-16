# Real-client findings — 2026-08-16

First live Hearthstone Battlegrounds session against the Debug capture build
(commit `04bcbdc`, Windows 11, Hearthstone at `D:\Hearthstone`, session log
`Logs\Hearthstone_2026_08_16_18_33_30`). Evidence lives in the operator's
private capture directory (`%LocalAppData%\IceCrow\private-captures\`, files
listed below) and in the session Power.log. Private evidence is not committed;
each finding lists the fixture to import after anonymization and review.

Session timeline: Hearthstone started 18:33 (before IceCrow, Scenario B);
IceCrow started ~18:37:35 and wrote `log.config`; the client applied it on the
fly at 18:38:22 without a restart; one real Battlegrounds match ran from
~18:39 onward.

Capture evidence files:

- `A` = `…f8e304….icecrow.json` — 260 events, saved 18:38:24.
- `B` = `…eac3cf….icecrow.json` — 61,543 events / 15.0 MB, saved 18:47:08.
- `C` = `…988f33….icecrow.json` — 260 events, identical shape to `A`,
  saved 18:47:08 (same second as `B`).

---

## F1 — False Power.log rewrite detection re-reads the file mid-match

**Severity: critical (splits and duplicates real matches).**

Evidence chain:

- The session Power.log contains exactly **one** `CREATE_GAME`
  (line 2, `D 18:38:22.8941919`, the client's catch-up dump).
- Capture `B` (the real match, turns advancing 18:39→18:47) ends with a
  `MatchEnded` whose timestamp is `18:38:22.8941919` — the timestamp of that
  first `CREATE_GAME` line, not of any live event.
- Capture `C` is a byte-similar duplicate of capture `A` (same 260 events,
  every event stamped `18:38:22.8941919`) and was saved in the same second
  as `B`.
- The real match was still running on screen after 18:47:08.

Interpretation: at ~18:47:08 `PowerLogTailer` concluded the log was
rewritten/rotated and re-read it from offset zero. Re-parsing line 2's
`CREATE_GAME` produced a `GameBoundary` that ended the live match capture
(`B`), re-processing the backlog produced a duplicate spurious match (`C`),
and the remainder of the real match continued into a new capture session.

Fix direction (owner: `IceCrow.Hearthstone.Logs` / `PowerLogTailer`):

1. Reproduce the false-positive trigger of the 64 KiB rewrite fingerprint /
   rotation heuristics against a growing real log (candidates: fingerprint
   window sampled while Hearthstone was mid-write; LastWrite/creation-time
   heuristics; share-mode timing).
2. Harden the heuristic so an append-only growing file is never classified
   as rewritten (e.g. verify the previous offset's suffix still matches
   before falling back to a full re-read; only re-read from zero when the
   file shrank or the fingerprint provably diverged).
3. Regression test: synthetic tailer test that appends in bursts while
   toggling timestamps/flush patterns recorded from this session.
4. Diagnostic: count full re-reads in tailer diagnostics so the developer
   window makes the next occurrence visible immediately.

## F2 — Client catch-up dump creates spurious zero-turn "matches"

**Severity: high (junk captures, false lifecycle starts).**

When Hearthstone applies `log.config` mid-session it dumps its current UI
context as a power snapshot: `CREATE_GAME` + `GameEntity` + 2 players + a
burst of tags, all sharing one timestamp. The Battlegrounds evidence
heuristic (`PLAYER_TECH_LEVEL` etc.) fires on this menu snapshot, producing
a "match" that starts and ends within one timestamp and records 260 junk
events (captures `A`, `C`).

Fix direction (owner: `IceCrow.Live` lifecycle + `IceCrow.App` capture
policy):

1. Strengthen match-start evidence: do not classify Battlegrounds from a
   snapshot that never advances a turn — e.g. require a first `TURN` change
   or mulligan/step progression after the tech-level evidence before
   arming a capture, or require more than two lobby players.
2. Capture-side guard regardless of lifecycle fix: discard (as a notice) a
   completed capture whose match never reached turn 1 — zero-turn evidence
   is never worth persisting.
3. Regression fixture: anonymized import of capture `A`
   (`real-menu-snapshot-001`, sourceType `real-anonymized`) asserting no
   match starts (after the fix) or that the capture is discarded.

## F3 — A legitimately saved real capture cannot be loaded back

**Severity: high (breaks replay/validation of real evidence).**

Capture `B` (15.0 MB on disk) was accepted by the write path (recorder
retained-bytes estimate under 32 MiB) but `RecordingSerializer.LoadAsync`
rejects it: `Recording exceeds the 33554432 preflight materialization-byte
limit`. Write-side and read-side estimates are inconsistent, so IceCrow can
produce recordings it refuses to replay — live↔replay parity fails exactly
on real matches.

Fix direction (owner: `IceCrow.Recording`):

1. Make the write path use the same estimate the read preflight uses (or a
   provably stricter one) so every recorder-accepted match round-trips; add
   a boundary round-trip test at the limit.
2. Recalibrate limits with real data: a 7-round real match produced 61,543
   events (56 K of them `RawTagChanged`) and 15 MB JSON. A long late-game
   match will plausibly exceed both the 32 MiB retained estimate and the
   64 MiB file cap — decide explicitly whether limits grow or the recorded
   stream gets a normalized reduction, and record the decision in
   `docs/resource-budgets.md`.
3. Until fixed, the analyzer's raw-JSON mode is the only way to inspect
   oversized captures; do not treat store-accepted files as replayable.

## F4 — Control events inherit the catch-up timestamp

**Severity: medium (misleading filenames and match metadata).**

All three captures are named `20260816T163822Z_…` and carry
`MatchStartedAt = 18:38:22.894` even though the real match started ~18:39.
The lifecycle candidate timestamp comes from the last `CREATE_GAME`, which
for this session was the catch-up dump line (and, via F1, the re-read line).
Largely a consequence of F1/F2, but worth an explicit decision: the
`StartBattlegrounds` observation could prefer the evidence event's
timestamp over a stale `CREATE_GAME` candidate when they diverge by more
than some bound. Re-evaluate after F1/F2 land; keep filenames unique-suffix
based regardless (already true).

## F5 — Overlay shows raw CardIds instead of hero names

**Severity: medium (MVP usability, not correctness).**

During the live match the opponent leaderboard rendered
`TB_BaconShop_HERO_25`, `BG22_HERO_000_SKIN_E`, `Player 12`, etc. — the
Manacost card database was not available/synced, so presentation fell back
to CardIds. Tracking and HP/armor values were correct.

Fix direction (owner: `IceCrow.App`/`Infrastructure.ManacostApi` +
`Presentation`): confirm from the developer window's Manacost panel whether
the cache was absent, stale, or the sync failed this session; if the data
boundary was healthy but hero-skin CardIds (`…_SKIN_…`) missed the lookup,
add skin→base-hero normalization to the lookup path. Add a presentation
test for the skin-id fallback either way.

## Positive observations (keep as MVP evidence)

- `log.config` written by IceCrow was applied by the running client within
  ~1 minute — no Hearthstone restart was required (runbook Scenario B note
  can be softened after one more confirmation).
- Scenario B (Hearthstone before IceCrow): log discovery, parsing, live
  tracking, overlay alignment, and click-through all worked; ~61.5 K events
  processed without safety rejections or observer detach.
- The hardened capture pipeline behaved as designed around the defects:
  session/persistence status stayed truthful, saves were sequential and
  atomic, and the bounded queue drained both pending captures in the same
  second without touching live tracking.

## Suggested fix order

1. F1 (tailer re-read) — corrupts every long session; blocks Gate B.
2. F3 (round-trip limits) — real captures must replay; blocks Gate C.
3. F2 (zero-turn spurious matches) — junk evidence and false lifecycle.
4. F5 (hero names) — MVP usability.
5. F4 (timestamps) — re-check after F1/F2.

Each fix lands with a regression test; F1–F3 additionally get anonymized
real fixtures imported from this session's captures after human privacy
review.
