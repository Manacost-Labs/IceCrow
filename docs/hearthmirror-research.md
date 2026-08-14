# HearthMirror client-state research

Date: 2026-08-14

Upstream revision inspected: [`HearthSim/Hearthstone-Deck-Tracker@649405176237b48e9c203ab5ffd95c7abc89c116`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/649405176237b48e9c203ab5ffd95c7abc89c116).

This document records current HDT behavior, not an IceCrow promise that every
Hearthstone build exposes the same memory layout. Power.log and
`TrackingSession` remain authoritative for historical match state. A future
client-state provider would only report the client's current UI state.

## Conclusions

- Current HDT uses a private `HearthMirror.dll` through small `HearthWatcher`
  polling adapters. It does not obtain these values from `Power.log`.
- The smallest useful IceCrow contract covers selected Battlegrounds mode,
  the currently hovered leaderboard entity, and the current generic choice UI.
- Current HDT does **not** prove that `GetCardChoices()` is a dedicated
  Battlegrounds hero-choice API. HDT derives offered BG heroes from game
  entities and uses `GetCardChoices()` for visible card-choice ordering and
  overlays. IceCrow therefore models generic choices, not “hero choices”.
- Current source does not expose a normal Battlegrounds tavern/shop API.
  `GetSpecialShopChoices()` is a different choice surface and is not evidence
  for a reliable shop snapshot.
- HearthMirror licensing and redistribution terms are not published in the
  inspected repository or binary bootstrap path. IceCrow must not download,
  reference, or redistribute the binary until HearthSim supplies explicit
  terms and a supported integration artifact.

## Capability matrix

| Priority | Capability | HDT requester and watcher | HearthMirror API and returned data | Schedule and change detection | Close/restart behavior | Power.log equivalent | IceCrow value | Fragility |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P0 | Selected BG mode | [`Watchers.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/Hearthstone%20Deck%20Tracker/Hearthstone/Watchers.cs#L401-L405) constructs `BaconWatcher`; [`SceneHandler.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/Hearthstone%20Deck%20Tracker/SceneHandler.cs#L93-L113) starts it in `BACON` and `GAMEPLAY`. | `Reflection.Client.GetSelectedBattlegroundsGameMode()` returns `UNKNOWN`, `SOLO`, or `DUOS`. | [`BaconWatcher.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/HearthWatcher/BaconWatcher.cs#L19-L52) polls every 200 ms and publishes only value changes. | All watchers stop and the IPC client stops when the Hearthstone window disappears; a later client start starts a new IPC client. | Partial. IceCrow infers an active BG match from tags, but cannot reliably identify the pre-lobby selected Solo/Duos UI state. | High: honest current pre-lobby/mode enrichment. | Medium: undocumented client memory and enum compatibility. |
| P0 | Hovered BG leaderboard entity | [`Watchers.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/Hearthstone%20Deck%20Tracker/Hearthstone/Watchers.cs#L56-L58) forwards changes to the overlay; `GameEventHandler` starts the watcher when a BG game starts or reconnects. | `Reflection.Client.GetBattlegroundsLeaderboardHoveredEntityId()` returns `int?`. | [`BattlegroundsLeaderboardWatcher.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/HearthWatcher/BattlegroundsLeaderboardWatcher.cs#L20-L54) polls every 16 ms and compares the nullable entity ID. | Global watcher stop clears its previous value; the next run publishes a fresh state. | No reliable equivalent. `Power.log` has entity state but not the user's current leaderboard hover. | High: can select an IceCrow `OpponentMemory` panel without taking focus or requiring ALT. The board still comes from `OpponentMemory`. | Medium/high: high-frequency, transient, undocumented UI memory. |
| P0 | Current choice UI | [`Watchers.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/Hearthstone%20Deck%20Tracker/Hearthstone/Watchers.cs#L48-L52) forwards visibility/card IDs to the overlay; the BG quest overlay also reads the API directly. | `Reflection.Client.GetCardChoices()` returns `CardChoices?` with `IsVisible` and ordered string `Cards`. | [`ChoicesWatcher.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/HearthWatcher/ChoicesWatcher.cs#L18-L52) polls every 16 ms and relies on `CardChoices` equality. The quest overlay temporarily polls visibility every 32 ms. | Stopped on leaving gameplay/global shutdown; the watcher's previous value is reset. | Partial. Offered entities can be logged, but exact current UI visibility and ordering are not always reconstructible. | High for future choice presentation, but v1 is generic choice state only; no recommendation/backend logic. | High: generic surface spans discover, trinket, quest, and possibly other choices; no hero-specific contract is proven. |
| P1 | Client scene | `SceneWatcher` feeds `SceneHandler`, which starts and stops feature watchers by `Mode`. | `Reflection.Client.GetSceneMgrState()` returns scene mode, previous mode, loaded and transition state. | Watcher-based polling and semantic transition handling; scene-specific watchers are not left running in every screen. | `SceneHandler.Reset()` and `Watchers.Stop()` run when the game window disappears. | Partial. Loading logs contain some scene transitions, but not a reliable current UI snapshot. | Medium: useful to scope future reads and prevent stale UI state. | Medium/high: client-only mode model and transition timing. |
| P1 | Lobby metadata | `BattlegroundsLobbyInfoWatcher` writes game metadata used for upload. | `Reflection.Client.GetBattlegroundsLobbyInfo()` returns game UUID and an ordered player list; HDT's comparer observes UUID, player count, and hero card IDs. | 200 ms; equality is intentionally a reduced semantic comparison. | Started only for BG gameplay/reconnect, stopped on scene exit/client close. | Mostly. Lobby players and heroes are reconstructed from tags; the client game UUID is not. | Low/medium for IceCrow UI because existing `TrackingSession` already owns lobby history. | High: opaque model and identity mapping; duplicating authoritative lobby state would be dangerous. |
| P2 | Duos teammate view/board | `GameEventHandler` starts `BattlegroundsTeammateBoardStateWatcher` only for Duos; `Watchers` updates HDT's Duos board state. | `Reflection.Client.GetBattlegroundsTeammateBoardState()` returns `ViewingTeammate`, mulligan hero IDs, and teammate entities/tags. | 200 ms with collection equality in event args. | Scene/client shutdown stops and clears watcher state. | Difficult/incomplete for the currently viewed teammate UI. | Medium, but outside the v1 Solo-focused contract. | High: large opaque entity collections, Duos-specific semantics, patch-sensitive tags. |
| SKIP | Normal tavern/shop snapshot | No current normal BG shop watcher/provider was found. `SpecialShopChoicesStateWatcher` is for special choice surfaces. | No inspected `GetBattlegroundsShop()` or equivalent call exists. | Not applicable. | Not applicable. | Partial entities may appear in logs, but reliable current freeze/order state was not established. | Potentially high later, but there is no sufficient source evidence now. | Very high; implementing it now would invent an API and semantics. |
| SKIP | Dedicated hero-selection choices | `HandleBattlegroundsStart` waits for hero entities and calls `SnapshotBattlegroundsOfferedHeroes`; it does not use a dedicated HearthMirror hero-choice API. | No dedicated API found. Generic `GetCardChoices()` is not sufficient evidence that every hero-selection flow is supported. | Power.log/entity-driven in current HDT. | Tracking lifecycle, not client-state lifecycle. | Yes for HDT's current implementation. | Already belongs to the deterministic tracking path. | Avoid duplicate events and false authority. |

## Actual call chain and lifecycle

The relevant current call chain is:

```text
HDT Core window lifecycle
  -> Reflection.StartIpcClient()
  -> scene/game handlers start selected HearthWatcher loops
  -> small provider property calls Reflection.Client.Get...()
  -> watcher compares current vs previous value
  -> HDT overlay or metadata consumer
```

[`Core.cs`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/649405176237b48e9c203ab5ffd95c7abc89c116/Hearthstone%20Deck%20Tracker/Core.cs#L390-L545)
uses the Hearthstone window lifecycle as the outer boundary. On close it stops
all watchers, resets scene/UI state, and calls `Reflection.StopIpcClient()`.
On a later start it calls `Reflection.StartIpcClient()` again. The public HDT
source exposes IPC logs, access-denied notification, IPC exit telemetry, and
memory-read counters, but the binary owns the process/session details.

This is evidence for the required IceCrow behavior—discard stale session state
after process loss—but it is not an API that IceCrow can safely copy or depend
on without the HearthMirror contract and license.

## Polling assessment

HDT's intervals are implementation evidence, not IceCrow defaults:

- selected BG mode, lobby metadata, and Duos teammate state: 200 ms;
- hovered leaderboard entity and generic choices: 16 ms;
- a BG quest-specific visibility loop: 32 ms for at most 120 seconds.

The first IceCrow layer contains no live adapter and therefore performs no
process polling. A future licensed adapter should cache slow reads and expose
one immutable combined snapshot. Proposed starting budgets, subject to real
measurement, are 500 ms for mode/scene, 250 ms for lobby, and 100 ms for hover
or visible choices only while BG-relevant. It must not poll every capability at
30/60 FPS. Identical semantic snapshots are suppressed by the coordinator.

No idle/BG CPU, allocation, or native read-duration claim is made in this
milestone because no HearthMirror adapter was legally integrated.

## Licensing and distribution decision

The current HDT repository does not publish a `LICENSE` file and its README
states “All Rights Reserved.” Its `licenses/` directory has no HearthMirror
entry. `Bootstrap.csproj` downloads `HearthMirror.x64.zip` directly from
`https://libs.hearthsim.net/hdt/`; HDT and HearthWatcher then reference
`lib/HearthMirror.dll` by file path and release scripts copy that binary into
the application. The official NuGet search API returned zero packages for
`HearthMirror` on 2026-08-14.

Observed compatibility facts:

- HDT's current HearthWatcher project targets .NET Framework 4.7.2 and x64;
- the bootstrap artifact is explicitly named `HearthMirror.x64.zip`;
- there is no package version in source, no public semantic-version contract,
  and no declared supported Hearthstone-build range;
- redistribution permission, attribution requirements, and independent-use
  support are not stated.

Decision: **do not bundle or reference HearthMirror**. Milestone 16 stops at an
IceCrow-owned abstraction, coordinator, and deterministic fake. A real adapter
requires written/public licensing terms, a supported artifact/versioning
contract, and architecture/runtime compatibility evidence.

## IceCrow implementation boundary

The contract project may represent only the proven P0 values:

- selected BG mode (`Unknown`, `Solo`, `Duos`);
- nullable hovered leaderboard entity ID;
- generic visible choice state with ordered card IDs.

It does not expose HearthMirror types, process handles, arbitrary tags, shop
entities, lobby history, opponent boards, or historical transitions.
`TrackingSession`, replay, reducers, and `OpponentMemory` remain entirely
independent.
