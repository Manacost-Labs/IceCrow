# Module boundaries

This document is the review checklist for every production project. The exact
project-reference graph is executable in `IceCrow.Architecture.Tests`.

| Module | Owns | May depend on | Must not own |
| --- | --- | --- | --- |
| `IceCrow.App` | WPF composition, process lifetime, concrete runtime wiring | UI/platform, live, recording, optional data/decks/telemetry boundaries | domain rules, parsing, durable formats |
| `IceCrow.Platform.Windows` | Win32 declarations, HWND discovery, hooks, modifier state | none | game semantics, WPF UI |
| `IceCrow.Overlay` | WPF windows, interaction fail-safe, design system and components | `Presentation`, `Platform.Windows` | parsing, entity reduction, HTTP |
| `IceCrow.Presentation` | immutable WPF-free UI projections, layout breakpoints, rendering policy | `Hearthstone.Data`, `Tracking` | controls, dispatchers, files, network |
| `IceCrow.Hearthstone.Logs` | `log.config`, log discovery, checkpointed bounded input | none | protocol semantics, WPF |
| `IceCrow.Hearthstone.Protocol` | defensive text-to-event normalization | none | entity state, Battlegrounds state, IO |
| `IceCrow.Hearthstone.ClientState` | optional immutable current-client observations | none | authoritative history, HearthMirror unless isolated adapter exists |
| `IceCrow.Hearthstone.Data` | static card/hero contracts and in-memory indexes | none | HTTP, disk paths, WPF |
| `IceCrow.Hearthstone.Decks` | IceCrow deck contracts and codec adapter | `Hearthstone.Data` | clipboard/UI, HTTP, match state |
| `IceCrow.Hearthstone.Entities` | canonical mutable entities and immutable snapshots | `Hearthstone.Protocol` | BG-specific policy, WPF |
| `IceCrow.Battlegrounds` | deterministic BG lobby/phase reduction | `Entities`, `Protocol` | presentation and strategy |
| `IceCrow.Battlegrounds.Memory` | immutable opponent boards and timelines | `Battlegrounds` | mutable entity references, UI |
| `IceCrow.Tracking` | single authoritative event-to-match transaction | protocol/entities/BG/memory | live files, WPF, feature registry, backend |
| `IceCrow.Live` | live lifecycle detection and single-consumer orchestration | logs/protocol/tracking | WPF, network, replay serialization |
| `IceCrow.Recording` | versioned recording and deterministic replay | tracking and typed domain contracts | live HWND/files, WPF, runtime type metadata |
| `IceCrow.Infrastructure.ManacostApi` | optional public HTTPS sync, validated cache, image cache | `Hearthstone.Data` | authoritative tracking, credentials |
| `IceCrow.Telemetry` | explicit consent, derived summaries, bounded local outbox | `Tracking` | raw logs, identifiers, enabled HTTP transport |

## Dependency decision test

Add code to an existing module when it shares that module's state owner and
trust boundary. Add a project only when it removes a real forbidden dependency
or isolates a new platform/security boundary. A folder, namespace, or concrete
class is enough for composition concerns that do not need assembly isolation.

Reject proposals named `Common`, `Utils`, `Helpers`, or `Core` unless the name
states a cohesive domain and the dependency graph proves why the assembly is
necessary. Cross-module coordination belongs in `App`, `Live`, or `Recording`,
not in a new global bus.

## Feature extension seam

Gameplay features consume `TrackingUpdate` for transition-specific work or
`TrackingSnapshot` for current immutable state. They do not add callbacks,
registries, UI state, or backend clients to `TrackingSession`. Presentation
features map snapshots in `Presentation` and render the resulting view state in
`Overlay`.
