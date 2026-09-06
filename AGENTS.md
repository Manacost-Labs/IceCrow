# IceCrow Engineering Guide

IceCrow is a Windows Hearthstone and Battlegrounds tracker. Keep the core deterministic, testable, and usable without a network or backend service.

## Clean-room boundary

The local Hearthstone Deck Tracker reference is at `../references/Hearthstone-Deck-Tracker`.

HDT is behavioral reference material only. It may be inspected to understand Hearthstone lifecycle transitions, GameTags, edge cases, and Windows overlay APIs. Never copy complete HDT files, copy large source blocks, mechanically rename HDT classes, or reproduce legacy architecture that IceCrow does not need. Prefer primary documentation when it is available and implement IceCrow behavior independently.

## Agent tooling and skill routing

This file is the canonical IceCrow policy for every AI agent (Claude Code,
Codex, Cursor, and others). `CLAUDE.md` only points here and carries
Claude-host-specific notes; do not duplicate project policy anywhere else.
The Manacost catalog profile for this repository is `icecrow` in
`Manacost-Labs/skills` (`profiles/icecrow.yaml`); profiles are on-demand
catalogs, not prompt bundles. A normal phase loads 1–3 skills and a complex
phase at most 4–5; write down a concrete reason before exceeding that.
Project `AGENTS.md` files stay more specific than any skill.

### Exact C# navigation

Use the C# language server (`csharp-lsp`) or Serena first for definitions,
references, callers, implementations, overrides, and type hierarchy. Use
grep/ripgrep for exact text, configuration, generated files, and Power.log
fixtures. Use Graphify or a code graph only when it saves context; never load
an entire graph into the conversation.

### Official .NET specialist skills

The official `dotnet/skills` marketplace (`dotnet-agent-skills`) provides the
required plugin groups `dotnet`, `dotnet-diag`, `dotnet-test`, and
`dotnet-msbuild`; `dotnet-advanced` is optional and only for the Windows native
boundary. Do not install MAUI/Blazor/AI plugins for this project. Route by
task and never load every group at once:

| Task | Load |
| --- | --- |
| Runtime performance, allocations, GC | `dotnet-diag/analyzing-dotnet-performance`, `dotnet-diag/dotnet-trace-collect`, `dotnet-diag/microbenchmarking` |
| Tests | `dotnet-test/run-tests`, `dotnet-test/assertion-quality`; optionally `dotnet-test/coverage-analysis`, `dotnet-test/find-untested-sources` |
| Build failures only | `dotnet-msbuild/binlog-generation`, `dotnet-msbuild/binlog-failure-analysis`, `dotnet-msbuild/build-perf-diagnostics` |
| Windows native boundary only | `dotnet-advanced/dotnet-pinvoke` |

`dotnet-artisan` may supplement WPF, concurrency, and testing guidance; when
it and `dotnet/skills` overlap, prefer the official plugin. IceCrow's own
`AGENTS.md` files, architecture docs, and tested invariants override generic
skill advice.

### Manacost skill router

| Phase | Load from the `icecrow` profile |
| --- | --- |
| Architecture or domain model | `engineering/codebase-design`, `engineering/domain-modeling`, `hearthpulse/arch-boundaries` |
| External API, profile sync, data source | `engineering/addy/api-and-interface-design`, `engineering/addy/source-driven-development`, `hearthpulse/api-contract-change` |
| New vertical slice | `hearthpulse/new-feature-slice`, `data/test-driven-development` |
| Debugging | `data/systematic-debugging`, `engineering/diagnosing-bugs`, `data/test-driven-development` |
| Performance | `engineering/addy/performance-optimization` plus `dotnet-diag/analyzing-dotnet-performance` and `dotnet-diag/dotnet-trace-collect` |
| Completion | `data/verification-before-completion`, `hearthpulse/verify-gate`, `hearthpulse/checkpoint-commit` |

The `hearthpulse/*` skills describe the HearthPulse repository; load them when
the task crosses into HearthPulse (ingestion contract, profile API, server
module) and apply their npm-based gates there, not to this .NET solution.

### Before editing

1. Read the owning module's guidance (nearest `AGENTS.md`, `docs/module-boundaries.md`).
2. Resolve exact symbols and references with the language server.
3. Inspect nearby tests and fixtures.
4. Inspect recent Git history if behavior is unclear.
5. Make the smallest coherent change.

### After editing

1. Run targeted tests, then the standard validation below.
2. Review diagnostics and warnings (warnings are errors in this solution).
3. Check model/file growth against the size discipline: models and view state
   preferred under 120 lines (review above 160, strong review above 220);
   services and engines preferred under 220 (review above 300, strong review
   above 400); methods preferred under 35 lines (review above 60, strong
   review above 90). No artificial fragmentation.
4. Review `git diff` and `git diff --check`.
5. Commit the logical unit; do not accumulate a giant uncommitted diff.

## Projects and responsibilities

| Project | Responsibility | Allowed IceCrow dependencies |
| --- | --- | --- |
| `IceCrow.App` | WPF composition root and application lifetime | `Overlay`, `Platform.Windows`, `Presentation`, `Hearthstone.Logs`, `Hearthstone.Data`, `Hearthstone.Decks`, `Hearthstone.ClientState`, `Infrastructure.ManacostApi`, `Live`, `Recording`, `Telemetry`, `ProfileSync` |
| `IceCrow.Platform.Windows` | Win32 and Windows-specific integration | None |
| `IceCrow.Overlay` | WPF overlay rendering and interaction boundary | `Presentation`, `Platform.Windows` |
| `IceCrow.Presentation` | Immutable WPF-free tracking/data-to-UI projections | `Hearthstone.Data`, `Tracking` |
| `IceCrow.Hearthstone.Logs` | Raw Hearthstone log input | None |
| `IceCrow.Hearthstone.Protocol` | Normalized game events and protocol compatibility | None |
| `IceCrow.Hearthstone.ClientState` | Optional current-client contracts and semantic change coordination | None |
| `IceCrow.Hearthstone.Data` | Offline static card/Battlegrounds metadata contracts and in-memory lookups | None |
| `IceCrow.Hearthstone.Decks` | IceCrow deck models and adapter to the canonical Deckstrings NuGet package | `Hearthstone.Data` |
| `IceCrow.Hearthstone.Entities` | Hearthstone entity state | `Hearthstone.Protocol` |
| `IceCrow.Battlegrounds` | Battlegrounds state and reducers | `Hearthstone.Entities`, `Hearthstone.Protocol` |
| `IceCrow.Battlegrounds.Memory` | Immutable historical Battlegrounds snapshots | `Battlegrounds` |
| `IceCrow.Tracking` | Authoritative deterministic match state processing | `Hearthstone.Protocol`, `Hearthstone.Entities`, `Battlegrounds`, `Battlegrounds.Memory` |
| `IceCrow.Live` | Non-UI live parsing, lifecycle detection, and tracking orchestration | `Hearthstone.Logs`, `Hearthstone.Protocol`, `Tracking` |
| `IceCrow.Recording` | Offline capture, replay navigation, and replay-specific safety limits | `Hearthstone.Protocol`, `Hearthstone.Entities`, `Battlegrounds`, `Battlegrounds.Memory`, `Tracking` |
| `IceCrow.Infrastructure.ManacostApi` | Public HTTPS dataset sync, last-known-good cache, and image disk cache | `Hearthstone.Data` |
| `IceCrow.Telemetry` | Consent-aware derived summaries and bounded offline outbox | `Tracking` |
| `IceCrow.ProfileSync` | Personal HearthPulse profile records, durable idempotent outbox, batched authenticated sync, device linking, protected credential contract | `Tracking`, `Hearthstone.ClientState` |

Test projects may reference only the production or developer-tool project under
test and its transitive dependencies. `IceCrow.App.Tests` is the Windows-only
home for direct application-runtime tests; it references `IceCrow.App` through
`InternalsVisibleTo` and no runtime project references it.

Developer-only tooling lives under `tools/` and must not be referenced by any
runtime project. `IceCrow.FixtureTool` may depend on `Recording` and `Live` to
build and validate regression candidates, but it must never auto-commit a
fixture or treat synthetic input as real evidence.

## Where changes go

- External log discovery/checkpointing belongs in `Hearthstone.Logs`; text
  normalization belongs in `Hearthstone.Protocol`.
- Authoritative atomic match transitions belong in `Tracking`; optional
  features consume `TrackingUpdate` or `TrackingSnapshot` outside that engine.
- Static card/hero contracts belong in `Hearthstone.Data`; HTTP/cache concerns
  belong in `Infrastructure.ManacostApi`.
- WPF-free UI mapping belongs in `Presentation`; controls and interaction belong
  in `Overlay`; process wiring and runtime ownership belong in `App/Runtime`.
- Recording/replay code must remain usable without live files, WPF, HWND, delay,
  or network access.
- Live match capture observes `IAppliedMatchEventObserver` on the live
  coordinator; the coordinator stays the only lifecycle authority and `Live`
  never references `Recording`. Capture composition (session, private store,
  status) lives in `App/Runtime`; an incomplete capture is discarded with a
  reason, never saved as complete evidence.

See `docs/module-boundaries.md`, `docs/threading-model.md`,
`docs/resource-budgets.md`, `docs/profile-sync.md`, and
`docs/feature-development.md` before changing a cross-module flow.

## UI rules

The visual language is defined in `docs/design-system.md`, the token set in
`docs/design-tokens.md` and `design/tokens.json`, and the rendering budget in
`docs/ui-performance-rules.md`. Architecture tests enforce the mechanical parts.

Before adding an overlay component:

1. Identify the immutable `ViewState` input it renders; presentation projections
   stay WPF-free and keep value equality.
2. Use the existing design tokens in `src/IceCrow.Overlay/Design`.
3. Use or extend an existing component before adding a new one. Every component
   needs a real consumer today.
4. Do not hardcode colours. `Design/Colors.xaml` is the only file allowed to
   contain colour literals.
5. Do not introduce heavy effects. No blur, no particles, no animated gradients,
   no looping storyboards, and at most one small shadow per floating panel.
6. Do not poll. React to view-state changes, not to a timer or a render loop.
7. Measure with `OverlayRenderDiagnostics` if the component is expected to
   update frequently.
8. Add the component and its states to the Debug design preview.
9. Check the common window sizes and 100–200% DPI.

## Dependency rules

- Domain projects must not reference WPF, `System.Windows`, window handles, or UI controls.
- UI code must never parse `Power.log` directly.
- Overlay code must consume presentation view state rather than domain stores or reducers.
- Raw log readers and parsers must never manipulate WPF directly.
- Keep normalized events between parsing and state reduction.
- Client-state snapshots are supplemental current UI state. They must not create
  or rewrite authoritative `TrackingSession` history.
- HearthMirror types, if a separately licensed adapter is added later, must stay
  inside `IceCrow.Hearthstone.ClientState.HearthMirror`.
- Avoid global mutable state and prefer immutable snapshots for historical data.
- New features consume `TrackingUpdate` or `TrackingSnapshot`; do not turn `TrackingSession` into a feature registry.
- Runtime projects must never reference developer-only projects under `tools/`.
- Keep numeric GameTag compatibility values inside explicit compatibility types.
- Core tracking must not require a network or backend service.
- Tracking, protocol, entities, Battlegrounds, decks, and overlay must not reference the Manacost HTTP adapter.
- No shared API token or admin credential may be embedded in IceCrow.
- Prefer the .NET BCL over new packages. Verify the version, license, and provenance before adding any package.
- Do not silently swallow important parser failures. Unknown Hearthstone events must not crash the tracker.
- Preserve uncertainty across layers: when a lower layer reports Exact/Likely/
  Unknown, a higher layer may simplify wording but must never silently increase
  certainty (see `docs/architecture.md`, "Uncertainty preservation").
- Do not introduce `Common`, `Utils`, service locators, global event buses, or
  generic repositories. Add a project only when it removes a forbidden
  dependency edge or protects a distinct platform/security boundary.
- Use bounded channels and bounded histories. A file-size limit does not replace
  an item, string, nesting, or work limit.
- `async void` is permitted only for UI event handlers. Blocking task waits are
  permitted only at a documented synchronous framework boundary.

## Source certainty

Every collected fact carries a typed certainty: `Exact`, `Partial`,
`Inferred`, or `Unknown`. Lower layers assign it from the evidence they own
and higher layers (presentation, profile sync, HearthPulse) may simplify the
wording but must never increase it. Tracking-level identity grades such as
`SameEntity`/`LikelySameCard` map to `Exact`/`Inferred`, never upward.

`Power.log` is authoritative for: match lifecycle, `PLAYSTATE` result,
turns, visible mulligan transitions, played and revealed cards, and
Battlegrounds placement and event history.

Current client state (a licensed HearthMirror-style adapter behind
`IceCrow.Hearthstone.ClientState`) is authoritative, only when available,
for: the own selected deck, the full collection, Arena draft and run state,
Arena rating, Battlegrounds MMR, and current client metadata.

Never report as `Exact`: a full opponent deck reconstructed from observed
cards, hidden opponent mulligan cards, Arena rewards without source
evidence, or rank/MMR values that no source actually exposed. An
archetype or deck guess stays `Inferred`; a value with no source stays
`Unknown` and is never fabricated. Personal profile data is synced only
through the authenticated `IceCrow.ProfileSync` boundary, never through the
anonymous telemetry outbox.

## Standard validation

Run from the repository root:

```powershell
dotnet restore IceCrow.sln
dotnet build IceCrow.sln --no-restore
dotnet test IceCrow.sln --no-build --no-restore
```

The complete gate also runs Release and repository hygiene:

```powershell
dotnet build IceCrow.sln -c Release --no-restore
dotnet test IceCrow.sln -c Release --no-build --no-restore
dotnet format IceCrow.sln --verify-no-changes --no-restore
git diff --check
```

Before reporting completion, review the complete diff and run security checks when the change introduces security-sensitive behavior.

## Regression policy

Every discovered Hearthstone bug or edge case must receive a regression test in the closest matching test project. The test should describe the observed input and expected normalized or reduced state without depending on WPF, a live Hearthstone process, or a backend service.
