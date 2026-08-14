# Feature extension guide

Start with the data a feature consumes and the owner of that data. Prefer a
class in an existing cohesive project; create a project only when it protects a
trust boundary or removes a real dependency edge.

## Typical extensions

| Feature | Correct home | Input |
| --- | --- | --- |
| New Power grammar | `Hearthstone.Protocol` | `RawLogLine` |
| Generic entity projection | `Hearthstone.Entities` | normalized `GameEvent` |
| Battlegrounds rule/state | `Battlegrounds` | mutation plus immutable entity snapshot |
| Historical opponent feature | `Battlegrounds.Memory` | tracking state transition |
| Analysis/statistics | a feature module above `Tracking` | `TrackingUpdate` / `TrackingSnapshot` |
| Overlay text/layout model | `Presentation` | `TrackingSnapshot` |
| WPF behavior | `Overlay` | presentation view state |
| Live file behavior | `Live` or `Hearthstone.Logs` | bounded external input |
| Replay navigation/format | `Recording` | versioned recording |

Good: a match-summary builder consumes immutable tracking updates and publishes
its own immutable summary. Bad: adding summary counters to `TrackingSession`,
calling WPF from a reducer, or parsing `Power.log` in a view model.

Good: add a Standard-mode reducer after its real lifecycle and state contract
are known. Bad: introduce `IGameMode`, a registry, and generic phase types while
Battlegrounds is the only implementation.

## Contract checklist

1. Identify the authoritative state owner and do not create a second writer.
2. Use normalized events between external parsing and reduction.
3. Copy mutable collections at historical and presentation boundaries.
4. Define hard limits for external strings, counts, queues, and retained state.
5. Keep optional providers supplemental and failure-isolated.
6. Add a regression test in the closest project; it may reference only that
   project directly.
7. Update the exact dependency-graph test when a justified project edge changes.
8. Document new public/versioned contracts and their compatibility policy.

## Future plugin and backend seams

Do not expose current public classes as a plugin API. A future plugin boundary
needs a small versioned immutable contract, capability declarations, time and
memory budgets, exception isolation, and an explicit load policy. Plugins must
consume snapshots; they must not receive mutable stores or native handles.

A future backend is an optional consumer above tracking. Network failures must
never block local log processing, replay, or overlay state. Upload requires an
explicit privacy model, queue limits, retry/backoff, and user-controlled
configuration. Domain projects remain network-free.
