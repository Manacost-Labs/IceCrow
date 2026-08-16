# Session A — real-client semantics and replay recovery

Sanitized engineering report. Session A of two concurrent sessions; owns
Recording, Battlegrounds(+Memory), Entities, Protocol, Tracking, FixtureTool.

## BASE COMMIT

`4f125cf8287cca3bc1dd298f29bec7d54bc7c72b`, branch
`fable/session-a-real-semantics-recovery`, worktree `../IceCrow-session-a`.

## PRIVATE EVIDENCE USED

One private real solo Battlegrounds capture ("capture B"): 61,543 events,
15.0 MB serialized, ~7 rounds over ~9 minutes, RawTagChanged-dominated
(55,997 of 61,543; 3,770 of them named-only references). Read-only; never
copied into the repository. No names or paths appear in this report.

## F3 ROOT CAUSE

The write path budgets 32 MiB with a per-event model (256 bytes base plus
2 bytes per string character), while the read preflight charged every JSON
token — schema property names, primitives, timestamps — against the same
32 MiB constant. The same data costs roughly 3–6x more under the token
model, so a writer-accepted real capture was unloadable.

## F3 FIX

`MaximumPreflightMaterializationBytes = 8 x MaximumRetainedBytes` with a
derivation comment (token overhead ≤ ~4x the write base; \uXXXX escaping
inflates non-ASCII to ≤ 3x the writer's per-character charge). Contract
tests: full-event-limit tag flood, escape-inflated Cyrillic, escape-heavy
ASCII, and a 9-million-token hostile flood that still fails fast.

## F7 ROOT CAUSE

`ReserveTimelineWork` charged `1 + total retained timeline events` after
every applied event — quadratic accumulation that rejected the real
capture at event 26,285 despite linear actual work.

## F7 FIX

The guard now charges one unit per event plus the timeline events that
event actually added; the four work-unit budgets moved into a
`ReplayLimits` record (structural caps stay constants). A
saturated-timeline 30k-event synthetic replays linearly; tightened budgets
still reject excessive work; `Reset` clears accounting.

## F6 TURN ROOT CAUSE

Two stacked gaps:

1. The real client references most entities by bare name
   (`Entity=GameEntity`, `Entity=Player#1234`); `EntityStoreReducer`
   required a numeric id and dropped every such change.
2. Even with a resolved mutation, `TrackingSession` built its
   Battlegrounds event from the raw event's entity id (null for bare
   names), so mutations never reached the Battlegrounds reducer.

`CalculateTurn((raw + 1) / 2)` is still correct: raw 13 = round 7 matched
the real match.

## F6 PHASE ROOT CAUSE

None beyond the above: compatibility tags 2022 (solo) and 3533 are alive
in the 2026 client (40 occurrences each on the game entity, 1→0 on combat
entry) and simply never arrived. No new phase tags were needed.

## ENTITY RESOLUTION FINDINGS

- `GameEntityDeclared` marks the game entity; the literal `GameEntity`
  resolves to it.
- Name→id associations are learned only from authoritative lines carrying
  both (bracketed descriptors, entity reveals); a name claimed by two ids
  becomes permanently ambiguous (duplicate minions never resolve by name).
- Unresolved named references are counted
  (`TrackingSession.UnresolvedNamedReferenceCount`) and dropped, never
  guessed. Real capture replay: 0 unresolved after the fixes.
- Name map bounded at 8,192 entries.

## OFFICIAL LOAD RESULT

`RecordingSerializer.LoadAsync` on capture B: success, 61,543 events,
~430–820 ms.

## OFFICIAL REPLAY RESULT

`ReplayRunner.RunAll` with default limits: success, 61,543 events in
~0.6–1.1 s (54k–104k events/s across runs).

## TURN / PHASE / OPPONENT BOARD CHECKPOINTS

Final replayed state of capture B: `turn=7`, `phase=GameOver`, lobby 9,
**7 opponents remembered** with boards captured on combat entry, 132
timeline events, 621 entities. The prefix-slice candidate replays its
explicit checkpoints `turn-1`, `first-combat`, `turn-2`, `second-combat`
(turn 2, phase Combat, 2 opponents at slice end).

## REAL FIXTURE STATUS

```text
REAL FIXTURE CANDIDATE READY
HUMAN PRIVACY REVIEW REQUIRED
NOT COMMITTED
```

Candidate `real-solo-turn-phase-board-001` (exact 7,181-event prefix slice
of capture B — order preserved, nothing rewritten, no fabricated end) was
anonymized and golden-validated by the existing FixtureTool importer into
a private candidates directory outside the repository. Automated leak scan
of the candidate: 0 BattleTag matches, 0 nickname matches, 0 Cyrillic
escapes. The full capture exceeds the importer's 256-checkpoint template
limit without explicit checkpoints — the slice carries four.

## PERFORMANCE

Capture B, Debug build: load ~0.4–0.8 s; replay ~0.6–1.1 s
(54k–104k events/s); 621 entities, 132 timeline events, 7 opponent
histories. No optimization performed beyond the accounting fix; no
order-of-magnitude regressions observed in the suite.

## SECURITY

`analyze-recording` prints aggregates only (names classified, unsafe
values redacted). Preflight remains a hostile-input bound: ≤ 64 MiB file,
≤ 256 MiB estimated token materialization, event/string/depth limits
unchanged. Diff-scoped security scan: see final session report.

## COMMITS

1. `47d54c5` — Add a private-safe real-capture analyzer command
2. `6c9f20a` — Unify recording write and read materialization budgets
3. `26249df` — Replace the replay timeline guard with actual bounded work accounting
4. `b2202d5` — Resolve bare-name entity references from proven associations
5. `f0bee4c` — Drive turn, phase, and board semantics from resolved mutations
6. (this report + resource budgets)

## OUT-OF-SCOPE NEEDS / HANDOFF TO SESSION B

- `LiveTrackingDiagnostics`/developer window may surface
  `TrackingSession.UnresolvedNamedReferenceCount` (new public property) as
  a privacy-safe counter — Live/App are Session B territory.
- F1 (tailer re-read) remains the top real-client blocker: it both splits
  captures and, amplified, discards the post-re-read half of a match via
  recorder limits. Session A's fixes make any capture that IS saved load
  and replay; Session B's tailer fix decides how much gets captured.
- F2 note: with real semantics restored, the zero-turn menu-snapshot
  "matches" are now cheaply detectable (a completed capture whose replay
  never leaves turn 0); the capture-discard policy for them lives in
  Session B's App layer.

## HANDOFF TO INTEGRATOR

- No shared/integration-owned files were edited. Session A touched only
  its owned paths plus `docs/resource-budgets.md` (owned) and this report.
- After merging both sessions, re-run the live↔replay parity suite and the
  real capture load/replay verification, then take the fixture candidate
  through human privacy review before any commit of
  `tests/fixtures/battlegrounds/real-solo-turn-phase-board-001`.
- The old `analyze-capture` scratch tooling outside the repository is
  superseded by the FixtureTool `analyze-recording` command.
