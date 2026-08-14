# ADR 0004: Overlay is presentation-only

## Context

The WPF overlay previously queried Battlegrounds state, opponent memory, and
timeline directly to construct display rows.

## Decision

`IceCrow.Presentation` maps immutable `TrackingSnapshot` values into immutable
overlay view state. `IceCrow.Overlay` receives that state and owns only window
lifetime, interaction, rendering, and click-through behavior. Native integration
remains in `Platform.Windows`.

## Consequences

UI mapping is testable without WPF and the overlay cannot reach domain stores.
The application composition root performs one explicit mapping step. The small
presentation assembly must not become an alternative game-state model.
