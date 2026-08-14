# ADR 0003: Live and replay share the tracking engine

## Context

Separate live and replay reducers would eventually produce different state for
the same normalized event stream.

## Decision

Both `LiveTrackingCoordinator` and `ReplayRunner` drive `TrackingSession`.
Recording owns format/navigation/work limits; Live owns log-source lifecycle and
buffering. Neither owns a second state engine.

## Consequences

Replay fixtures test the same semantics used live, and fixes need one reducer
path. Source-specific safety and lifecycle concerns remain in their adapters.
