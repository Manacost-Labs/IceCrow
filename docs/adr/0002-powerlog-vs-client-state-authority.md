# ADR 0002: Power log history versus client-state enrichment

## Context

Power events reconstruct historical game truth. Optional client-state providers
can observe current hover, choices, or UI mode, but may be unavailable and have
different licensing and failure characteristics.

## Decision

`TrackingSession` history is authoritative. `Hearthstone.ClientState` exposes
independent immutable current-state contracts. Provider observations may enrich
presentation only and cannot create or rewrite historical tracking events.

## Consequences

Core tracking and replay remain offline and provider-independent. Optional
adapters are isolated assemblies and failures degrade enrichment, not match
reconstruction. Some current-client details may be absent without a provider.
