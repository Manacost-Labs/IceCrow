# Tracking boundary

`TrackingSession` is the deterministic single writer for authoritative match
state. It consumes normalized events only and publishes immutable updates and
snapshots. Do not add UI, files, HWNDs, HTTP, telemetry, strategy, plugin
registries, or feature callbacks. New features consume `TrackingUpdate` or
`TrackingSnapshot`. Preserve finite entity/tag/lobby/timeline/history budgets
and make limit rejection deterministic and testable.
