# Live Hearthstone acceptance checklist

Date prepared: 2026-08-14

Status: **NOT YET VERIFIED**. This checklist has not been executed against a real Hearthstone client. Automated tests prove the in-process `RawLogLine → parser → live coordinator → TrackingSession` path only.

## Real-client matrix

- [ ] Launch IceCrow before Hearthstone; detection and overlay connect after the client starts.
- [ ] Launch Hearthstone before IceCrow; detection and overlay connect.
- [ ] Start a Battlegrounds match; live tracking becomes active only after Battlegrounds-specific evidence.
- [ ] Hero selection is detected.
- [ ] The lobby populates with the expected players.
- [ ] Battlegrounds turns increment correctly.
- [ ] The current opponent is detected.
- [ ] Recruit-to-combat transition is detected.
- [ ] The opponent board is saved on combat entry.
- [ ] Combat-to-recruit transition is detected.
- [ ] The overlay updates from immutable live snapshots without stealing focus.
- [ ] Match end hides stale live lobby state while diagnostics remain available in Debug.
- [ ] A second match starts with no entity, opponent-memory, or timeline leakage.
- [ ] Restart Hearthstone between games; the new log/session is discovered.
- [ ] Power.log rotation/restart resumes without rereading the complete file or duplicating state.
- [ ] Alt-tab and minimize/restore preserve click-through, focus, and visibility behavior.

## Match capture matrix

- [ ] With capture disabled, a complete match creates no files under `%LocalAppData%/IceCrow/private-captures/`.
- [ ] With capture enabled before a match, status moves Waiting → Recording → Saved and exactly one capture file appears.
- [ ] The saved capture validates and replays to a final state equivalent to the live session (spot-check with FixtureTool import).
- [ ] A second match without restarting IceCrow creates a separate capture file.
- [ ] Match end occurs once; no capture events are appended after it.
- [ ] Forcing a capture directory failure (e.g. read-only root) reports Failed status while live tracking continues.
- [ ] Closing IceCrow mid-match discards the in-flight capture instead of saving a partial file.
- [ ] Capture filenames contain only a UTC timestamp and random identifier.

## Proven automated fallback behavior

- `CREATE_GAME` creates an explicit normalized boundary and clears parser block/entity context.
- A match is not classified as Battlegrounds merely because a game entity or ordinary tag appears.
- The current Power.log-only fallback starts BG tracking only after one of the named BG signals `PLAYER_TECH_LEVEL`, `PLAYER_TRIPLES`, or `NEXT_OPPONENT_PLAYER_ID`.
- `NEXT_OPPONENT_PLAYER_ID` identifies the local player entity, matching the inspected HDT handling path.
- `STATE=COMPLETE` or a supported terminal local `PLAYSTATE` ends the live tracking session.
- A later `CREATE_GAME` followed by BG evidence resets entities, opponent memory, and lobby timeline before applying the new match.

## Known signal gaps

- Exact Hearthstone game type and authoritative local player identity are not available in IceCrow's accepted Power.log subset. Current HDT obtains those from client reflection / match information, so IceCrow does not invent a `GAMETYPE` meaning.
- If IceCrow attaches to a match after its decisive BG signals have already passed and the log does not replay them, classification can remain inactive.
- A reconnect that emits `CREATE_GAME` rebuilds state from subsequent/replayed log events. If Hearthstone supplies only truncated history, earlier lobby or board history cannot be reconstructed.
- Undocumented phase tags remain compatibility signals with pinned HDT-source provenance and can change after Hearthstone updates.
