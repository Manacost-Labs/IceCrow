# ADR 0001: One authoritative tracking session

## Context

Live processing, replay, tests, and future consumers need identical match state
for the same ordered normalized events.

## Decision

`TrackingSession` is the single authoritative event-to-state engine. Live and
replay adapt their input into its lifecycle and `GameEvent` methods and consume
immutable updates/snapshots.

## Consequences

State semantics are deterministic and cannot drift between live and replay.
New features consume outputs rather than adding parallel reducers. The session
remains Battlegrounds-oriented until another real mode proves a shared model.
