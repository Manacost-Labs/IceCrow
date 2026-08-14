# Feature development guide

## Choose the seam

1. Decide whether the feature needs normalized transitions (`TrackingUpdate`),
   current immutable state (`TrackingSnapshot`), optional static data, or only a
   presentation view state.
2. Select the owning module from `module-boundaries.md`.
3. Keep external IO in its boundary and pass typed immutable values inward.
4. Add a new project only if a forbidden dependency or distinct trust boundary
   cannot be expressed by a folder/namespace.

## Implementation rules

- Do not add a feature registry, mediator, global event bus, service locator, or
  mutable singleton.
- Do not teach `TrackingSession` about exports, UI panels, telemetry, strategy,
  data refresh, or backend calls.
- Put numeric compatibility tags in one explicit compatibility type with source
  provenance.
- Bound every external string, collection, queue, history, retry, and work loop.
- Historical objects copy collections and never retain `GameEntity` references.
- Presentation factories are WPF-free; controls receive ready-to-render values.
- Optional data and network failures never stop local tracking.

## Test route

Add the smallest test in the project under change. Hearthstone edge cases get a
normalized fixture/regression that runs without Hearthstone, WPF, HWND, delay,
or network. Update architecture tests when the dependency graph or a forbidden
pattern changes. Run focused tests, then Debug/Release solution tests, format,
whitespace, performance diagnostics when a hot path changed, and the appropriate
security scan for external/native/input changes.

## Review questions

- Who owns the mutable state and on which thread?
- What happens on cancellation, replay, reconnect, malformed input, and missing
  optional data?
- What is the hard resource budget and how is the rejection observable?
- Is the output deterministic and immutable?
- Can this feature be removed without changing the tracking engine?
