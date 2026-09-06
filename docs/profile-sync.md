# Personal profile sync (HearthPulse companion)

IceCrow can run as a headless companion that collects the local player's
Hearthstone results and syncs them to the user's HearthPulse profile. This
document is the contract: what is collected, from which source, with which
certainty, how it is stored locally, how it is authenticated, and what the
resource budgets are. Anonymous telemetry (`docs/telemetry.md`) is a separate
boundary and is never used for personal data.

## Product defaults

| Feature | Release default | Source of truth |
| --- | --- | --- |
| Overlay | off (`overlayEnabled: false`) | `IceCrowRuntimeOptions`, `%LOCALAPPDATA%\IceCrow\settings.json` |
| Power.log tracking | on | `IceCrow.Live` |
| Profile sync | on, inert until the device is linked | `IceCrow.ProfileSync` |
| Debug capture | Debug-only, unchanged | `IceCrow.Recording` |

When the overlay is disabled the runtime never calls `OverlayComposition`,
so `IceCrow.Overlay.dll` and `IceCrow.Presentation.dll` are not loaded and no
WPF dispatch happens per tracking snapshot. `App.xaml` carries no
application-level resource dictionary for the same reason. The Debug build
defaults the overlay on to keep the developer window and design preview.

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
| Own selected deck, collection, Arena draft/run/rating, Battlegrounds MMR | current client state through `IceCrow.Hearthstone.ClientState` sources; **no HearthMirror adapter ships** (`docs/hearthmirror-research.md`) | Unknown / null |

Player names, account ids, raw `Power.log`, and server game handles are never
stored. `gameJoinEvidence` stays null until an authoritative handle source
exists; IceCrow never fabricates a join key from timestamps.

## Records and events

Wire records live in `src/IceCrow.ProfileSync/Records`. Each finished record
becomes one `ProfileEvent` (`eventId` UUIDv7, `type`, `schemaVersion` 1,
`occurredAt`, `payload`) serialized exactly once. Types:
`constructed_match`, `arena_match`, `arena_run`, `arena_draft_pick`,
`battlegrounds_match`, `collection_snapshot`. The HearthPulse contract is
`docs/specs/tracker-profile-ingestion-v1.md` in the HearthPulse repository.

## Local outbox

`ProfileOutbox` is one JSON array file at `%LOCALAPPDATA%\IceCrow\profile\outbox.json`,
written atomically (temp file + move) behind a single gate.

| Budget | Value |
| --- | --- |
| History events (matches, Arena) | 256; a full outbox returns an explicit `Full` result that the runtime counts and reports, never a silent drop |
| Collection snapshots | latest-only; a newer pending snapshot replaces the older one |
| Payload per event | 512 KiB (collection snapshot 4 MiB); null optional fields are omitted on the wire |
| File | 16 MiB; a larger or malformed file is `InvalidDataException`, never partially trusted |
| Upload batch | 25 events (hard cap 50) |
| Producer channel in the App | 64 events, `DropWrite` with a counted overflow |

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

Collection reads happen only on triggers (startup, Collection Manager exit,
Pack Opening exit, manual refresh) and are hash-deduplicated; there is no
continuous collection polling. The Arena watcher is asleep unless a draft is
active and then polls at a bounded 500–1000 ms interval (`ArenaWatchPolicy`).
Battlegrounds rating is read only at match boundaries. All of this stays inert
until a licensed client-state adapter exists.

## Performance evidence

See `docs/performance.md` ("Headless companion") for the measured Release
baseline and post-change numbers produced by `tools/perf/Measure-IdleProcess.ps1`.
