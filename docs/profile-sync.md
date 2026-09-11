# Personal profile sync (HearthPulse companion)

IceCrow runs as a local companion that collects the local player's Hearthstone
results, keeps a permanent on-device history, and can sync them to the user's HearthPulse profile. This
document is the contract: what is collected, from which source, with which
certainty, how it is stored locally, how it is authenticated, and what the
resource budgets are. Anonymous telemetry (`docs/telemetry.md`) is a separate
boundary and is never used for personal data.

## Product defaults

| Feature | Release default | Source of truth |
| --- | --- | --- |
| Overlay | off (`overlayEnabled: false`) | `IceCrowRuntimeOptions`, `%LOCALAPPDATA%\IceCrow\settings.json` |
| Power.log tracking | on | `IceCrow.Live` |
| Local history UI/archive | on | `IceCrow.App`, `IceCrow.ProfileSync.History` |
| Profile sync | on, inert until the device is linked | `IceCrow.ProfileSync` |
| Debug capture | Debug-only, unchanged | `IceCrow.Recording` |

When the overlay is disabled the runtime never calls `OverlayComposition`, so
no WPF dispatch happens per tracking snapshot. The normal history window still
uses the shared IceCrow design resources. The Debug build additionally defaults
the in-game overlay on and opens the developer diagnostics window.

## Sources and certainty

Every collected value carries a typed certainty (`Exact`, `Partial`,
`Inferred`, `Unknown`, see `IceCrow.ProfileSync.Certainty`). Lower layers assign
it; mapping into records may only keep or lower it.

| Fact | Authoritative source | Certainty when the source is missing |
| --- | --- | --- |
| Match lifecycle, result (`PLAYSTATE`), turns, duration | `Power.log` | Unknown |
| Game type, format, build, scenario | `GameState.DebugPrintGame()` lines (`GameMetadataObserved`) | mode Unknown, match ignored |
| Own mulligan (initial, kept, replaced, after) | `Power.log` `MULLIGAN_STATE` transitions | Unknown |
| Opponent mulligan | `Power.log`, replaced count only | null |
| Opponent deck | observed card ids only (`Partial`), never a code | Unknown |
| Battlegrounds placement | `PLAYER_LEADERBOARD_PLACE` on the local player | Unknown |
| Battlegrounds final board | own warband at the first attack of each combat, frozen at completion (`Exact` only from the final turn, else `Partial`) | null |
| Own selected deck, Arena draft/run/rating, Battlegrounds MMR | current client state through `IceCrow.Hearthstone.ClientState` sources; **no HearthMirror adapter ships** (`docs/hearthmirror-research.md`) | Unknown / null |
| Owned collection | complete Manacost HDT Collection Exporter schema-v3 JSON snapshot | Exact at `exportedAt`; unavailable before the first export and stale after later client changes |

Player names, account ids, raw `Power.log`, and server game handles are never
stored. `gameJoinEvidence` stays null until an authoritative handle source
exists; IceCrow never fabricates a join key from timestamps.

## Data flow

```text
Power.log
  -> PowerLineParser (one parse per line)
  -> GameSessionCoordinator (IceCrow.Live)
       route by GameMetadataState.Mode:
       Battlegrounds / Duos -> LiveTrackingCoordinator -> TrackingSession (existing engine)
       Ranked / Arena       -> ConstructedMatchTracker (bounded entity table, mulligan, result)
       Casual / other       -> ignored (boundary only)
  -> ProfileRecordPipeline (IceCrow.App)
       completed ranked game     -> ConstructedRecordFactory.CreateRanked -> constructed_match
       completed Arena game      -> ConstructedRecordFactory.CreateArena + ArenaRunCollector.Associate -> arena_match
       ended Battlegrounds match -> BattlegroundsRecordFactory.Create -> battlegrounds_match
  -> local ProfileHistoryWorker -> permanent bounded history -> History UI
  -> optional ProfilePersistenceWorker -> ProfileOutbox journal -> ProfileSyncCoordinator -> HearthPulse
```

Constructed and Arena games never construct the Battlegrounds
`OpponentMemory`/board-diff engine; the Battlegrounds coordinator only starts
a `TrackingSession` on its own confirmed evidence. Uploads are held while any
routed game is open and resume when it ends.

## Records and events

Wire records live in `src/IceCrow.ProfileSync/Records`. Each finished record
becomes one `ProfileEvent` (`eventId` UUIDv7, `type`, `schemaVersion` 1,
`occurredAt`, `payload`) serialized exactly once. Types:
`constructed_match`, `arena_match`, `arena_run`, `arena_draft_pick`,
`battlegrounds_match`, `collection_snapshot`. The HearthPulse contract is
`docs/specs/tracker-profile-ingestion-v1.md` in the HearthPulse repository.

## Local outbox

The permanent history is `%LOCALAPPDATA%\IceCrow\history\matches.jsonl`.
It is a separate, idempotent JSON Lines archive capped at 4096 events and
64 MiB. It recovers an incomplete final line, rejects interior corruption,
and publishes immutable projections for the UI. A server acknowledgement never
removes this file. The upload outbox below remains transient by design.

`ProfileOutbox` keeps history in `%LOCALAPPDATA%\IceCrow\profile\outbox.jsonl`
and the newest pending collection in `collection-pending.json`, behind a single
gate. A legacy `outbox.json` is imported once.

| Budget | Value |
| --- | --- |
| History events (matches, Arena) | 4096 in an append-only JSON Lines journal (`outbox.jsonl`, one flushed line per event, acknowledgements as tombstones, compaction after 256 tombstones); a full outbox returns an explicit `Full` result that the persistence worker holds and reports, never a silent drop |
| Collection snapshots | latest-only; a newer pending snapshot replaces the older one |
| Payload per event | 512 KiB (collection snapshot 4 MiB); null optional fields are omitted on the wire |
| File | 64 MiB journal; a crash-truncated final line is dropped and counted, any other corruption is `InvalidDataException`, never partially trusted; a pre-journal `outbox.json` is imported once |
| Upload batch | 25 events (hard cap 50) |
| Producer handoff | 64 events owned by a single persistence worker until the durable commit succeeds; transient IO failures are retried with backoff; a full handoff is refused explicitly (`Full`), the oldest accepted events are preserved; shutdown completes the producer side and drains accepted events within a 10 s grace before the uploader stops |

## Upload pacing

`ProfileSyncCoordinator` is the only uploader. It wakes on a new event or
every 5 minutes, holds while a match is in progress, uploads bounded batches,
and on failure backs off exponentially from 30 s to 30 min with ±20% jitter
(or honours `Retry-After`). Acknowledged and permanently rejected events are
removed; everything else is retried with the same `eventId`, so retries are
idempotent server-side. Zero HTTP requests happen per gameplay event.

## Authentication

Linking uses the HearthPulse OAuth 2.0 device authorization flow
(`/api/v1/oauth/device/code`, `/api/v1/oauth/token`) with client id
`manacost-tracker` and the least-privilege scopes `profile.read tracker.write`.
Start it with `IceCrow.App.exe --link-hearthpulse`; `--unlink-hearthpulse`
revokes the refresh-token family and clears the local credential. The
credential (access token, rotating refresh token, scopes, origin) is stored
only through `ProtectedProfileCredentialStore` behind Windows DPAPI
(`WindowsDataProtection`, current user, purpose-bound entropy). A tampered or
oversized credential file is rejected as invalid. No shared or admin token
exists in the client. A 401 triggers one refresh; a rejected refresh clears the
credential and the coordinator waits for the user to link again.

## Client-state watchers

The working collection source reads a complete schema-v3 file from the Manacost
HDT Collection Exporter. It auto-discovers the newest documented export at
startup and supports `--import-collection <path>` and
`--refresh-collection`. Reads are hash-deduplicated and never polled. Personal
metadata in the exporter document is ignored. Collection Manager / Pack Opening
exit triggers remain reserved for a future licensed live adapter. See
`docs/collection-source-research.md`.

The Arena watcher is asleep unless a draft is
active and then polls at a bounded 500–1000 ms interval (`ArenaWatchPolicy`).
Battlegrounds rating is read only at match boundaries. Those client-memory
features stay inert until a licensed client-state adapter exists.

## Performance evidence

See `docs/performance.md` ("Headless companion") for the measured Release
baseline and post-change numbers produced by `tools/perf/Measure-IdleProcess.ps1`.
