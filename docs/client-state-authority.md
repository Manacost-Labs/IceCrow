# Client-state authority policy

Date: 2026-08-14

IceCrow has two deliberately separate state sources:

```text
Power.log -> parser -> TrackingSession -> historical deterministic state

licensed client-state adapter -> ClientStateSnapshot -> current UI enrichment
```

Client state is supplemental. Provider failure must never stop Power.log
tailing, tracking, recording, or replay.

## Authority matrix

| Category | Authority | Client-state role | Conflict policy |
| --- | --- | --- | --- |
| Match start/end and match identity | `TrackingSession` from normalized Power.log events | None | Ignore client-state claims about history. |
| Turn | `TrackingSession` | May display current client context later, but cannot change turn history | Tracking value wins. |
| Recruit/combat/game-over history | `TrackingSession` and `BattlegroundsReducer` | May gate transient UI reads only | Tracking value wins; never emit a reducer event from a snapshot. |
| Lobby history, tier, triples, health, armor, death | `TrackingSession` and `LobbyTimeline` | May confirm current UI state in a future presentation model | Never add timeline events from client state. |
| Opponent last-seen board and board age | `OpponentMemory`, captured on tracked combat entry | Selects which existing snapshot to render | Never replace board/minions from client state. |
| Selected BG pre-lobby mode (Solo/Duos) | Client state while available | Current client UI enrichment | Report unavailable/unknown on failure; do not infer match history. |
| Currently hovered BG leaderboard entity | Client state | Selects an opponent detail panel | Clear on disconnect/unavailable; never create an opponent/entity. |
| Current choice UI visibility and ordering | Client state | Current presentation only | Clear on disconnect/unavailable; never create historical choice events. |
| Current client scene | Client state if added later | Scopes polling and presentation | Absence never resets `TrackingSession`; a proven Power.log match lifecycle remains authoritative. |
| Current normal tavern shop | No source accepted in v1 | Not implemented | Do not infer or synthesize. |

## Reconciliation rules

1. A `ClientStateSnapshot` is immutable current-state data, not an event.
2. The client-state coordinator performs semantic change detection and does not
   publish timestamp-only duplicates.
3. `Unavailable`, `Disconnected`, and `Unsupported` snapshots contain no stale
   Battlegrounds UI state.
4. Capability loss is local: if choices become unavailable, BG mode or hover
   may still remain available.
5. A process/session restart must pass through a non-connected state or a new
   adapter session and must not reuse the old snapshot.
6. No client-state DTO is accepted by `TrackingSession`, protocol reducers,
   `EntityStore`, `BattlegroundsReducer`, `LobbyTimeline`, or replay.
7. Presentation may combine `HoveredEntityId` with a read-only
   `OpponentMemory` lookup. This is a view selection, not domain mutation.

## Failure behavior

If the optional provider fails:

- provider-specific UI reports client integration unavailable;
- hover and choice enrichment clear;
- Power.log turn/phase tracking continues;
- lobby history, opponent memory, and timeline continue;
- recording and offline replay continue without Hearthstone, WPF, network, or
  HearthMirror.

The current milestone intentionally ships no HearthMirror adapter because its
licensing and redistribution terms are unclear. The contracts can be exercised
with a deterministic fake without pretending that live integration exists.
