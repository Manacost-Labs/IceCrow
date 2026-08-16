# IceCrow MVP 0.1 scope

A narrow internal Windows MVP validated against the real Hearthstone client.
Anything not listed as in scope is out of scope by default.

## In scope

- Windows 10/11, win-x64, .NET 10 Debug/Release builds.
- Hearthstone Battlegrounds **Solo** only.
- Automatic Power.log discovery and `log.config` configuration.
- Match start/end detection from Battlegrounds evidence.
- Lobby state, turn and phase, current opponent.
- Opponent Memory with the last observed opponent board and board-change
  diffs that preserve exact/likely/ambiguous confidence.
- Lightweight overlay: click-through, focus-safe, no render loop.
- Offline card metadata fallback when the Manacost API is unavailable.
- Debug-only private match capture for regression evidence, with the
  session/persistence status model and bounded retention.
- Deterministic replay and committed synthetic/real-anonymized fixtures.
- Clean startup/shutdown, including shutdown mid-match.
- Two consecutive matches in one process without state leakage.

## Explicitly out of scope for MVP 0.1

- Combat odds or any simulator output.
- Battlegrounds Duos; mobile; macOS.
- Constructed deck tracking.
- Account sync, login, or public telemetry upload.
- Advanced analytics, composition detection, threat score, Hero Picker.
- Installer or auto-update.
- Every Hearthstone edge case; perfect late-attach or reconnect
  reconstruction (documented as known limitations instead).

## Readiness gates

Defined and tracked in [mvp-readiness.md](mvp-readiness.md):

- **Gate A — build**: Debug+Release+formatter+CI+soak green.
- **Gate B — real client**: one complete and two consecutive real matches,
  validated capture, no focus stealing, aligned overlay, API-offline safety.
- **Gate C — evidence**: at least two reviewed real-anonymized fixtures with
  explicit source types and no private data in the repository.
- **Gate D — performance**: no render loop, no sustained unexplained CPU, no
  unbounded queue/cache, verified capture-disabled overhead, no obvious
  retained growth across two matches.
- **Gate E — known limitations**: documented, visible to the tester, no
  false certainty.
