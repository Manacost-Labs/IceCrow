# Error boundaries and diagnostics

IceCrow separates failures by origin so expected external faults do not crash
the tracker and programming faults are not silently hidden.

| Category | Examples | Boundary behavior |
| --- | --- | --- |
| Expected external state | Hearthstone/log file absent, file locked, window closed, provider unavailable | retry or disconnect with cancellation-aware bounded backoff; preserve a useful diagnostic state |
| Malformed/untrusted input | unknown Power line, invalid replay field, partial line | ignore/mark malformed or reject the file; never enter reducers with an invalid object |
| Safety limit | excessive entities, tags, strings, replay work, queue pressure | deterministic drop/reject/fault result with bounded counters |
| Programming fault | invariant violation, impossible state, invalid internal argument | fail the operation visibly; do not convert it to an ordinary malformed-input counter |

## Boundary ownership

- `Hearthstone.Logs` owns filesystem exceptions and log-generation recovery.
- `Hearthstone.Protocol` owns line classification and parser diagnostics.
- `Live` owns pre-detection overflow and live safety-limit reporting.
- `Tracking` converts typed resource-limit failures into deterministic results;
  it does not catch arbitrary exceptions.
- `Recording` validates the full untrusted envelope before replay and enforces
  replay work budgets.
- `ClientStateCoordinator` isolates an optional provider and preserves the last
  known semantic state according to its contract.
- `Platform.Windows` owns native error conversion and disposable hooks.
- `Overlay` catches native interaction failures only to restore click-through;
  it then surfaces the failure to the composition boundary.
- `App` coordinates shutdown and reports top-level background failures.

## Diagnostic contract

Diagnostics are observations, not domain state. Counters are monotonic within
their owner and reset only with that owner. Details from hostile input are
length-bounded and repeated malformed patterns are sampled/coalesced. Hot-path
diagnostic getters must be O(1); they must not scan all entities or histories
for every raw line. Never include account identifiers, raw tokens, secrets, or
unreviewed full recordings in routine logs.

Use these stable diagnostic categories independent of any logging vendor:
`Platform`, `Logs`, `Protocol`, `Tracking`, `Battlegrounds`, `ClientState`,
`Overlay`, and `Recording`. A future sink receives structured category, code,
severity, timestamp, and bounded properties; domain code does not depend on the
sink implementation.

Cancellation and disposal are not errors. Every owner that creates a stream,
hook, timer, watcher, or background task must stop and dispose it. WPF
`async void` is reserved for UI event/lifetime handlers where exceptions are
handled at the application boundary.
