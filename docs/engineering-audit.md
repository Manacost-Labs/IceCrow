# Engineering audit

Date: 2026-08-14
Scope: IceCrow `main` at `9ec2449`, before the milestone refactoring.

## Executive conclusion

IceCrow already has a modular, acyclic production graph. Domain assemblies are independent from WPF, Win32, HTTP, and live Hearthstone state. The most valuable change is therefore not another project or a generic abstraction layer: it is to split the WPF composition root into small concrete runtime coordinators while preserving the existing project boundaries.

The audit found three reproducible robustness defects at untrusted-input boundaries. They are included in this milestone because they affect long-run reliability and have narrow regression tests:

1. A timestamp-only update of an unchanged `Power.log` at end-of-file can reset the checkpoint and replay the whole file.
2. `TelemetryOutbox` deserializes the complete root array before enforcing its 128-item limit.
3. Explicit `null` members in the Hearthstone data cache can escape as `ArgumentNullException` instead of the documented `InvalidDataException`.

No evidence supports creating a service locator, event bus, shared `Common`/`Utils` project, generic repository, or new production assembly.

## Baseline quality gate

| Check | Result |
| --- | --- |
| SDK | .NET SDK 10.0.302, host 10.0.10 |
| Restore | Passed, 5.32 s |
| Debug build | Passed, 28.70 s, 0 warnings, 0 errors |
| Debug tests | 200 passed, 46.99 s |
| Working tree | Clean before changes |
| Remote | `main` matched `origin/main` |
| GitHub Actions | Latest run passed |

The timings are local wall-clock observations, not stable benchmarks. They are useful for detecting order-of-magnitude regressions only.

## Production dependency graph

The solution contains 17 production projects and 17 matching test projects. Edges point from consumer to dependency.

```text
IceCrow.App
  -> Overlay, Platform.Windows, Presentation, Hearthstone.Data,
     Hearthstone.Decks, Infrastructure.ManacostApi, Telemetry,
     Hearthstone.Logs, Live, Recording
IceCrow.Overlay -> Platform.Windows, Presentation
IceCrow.Presentation -> Hearthstone.Data, Tracking
IceCrow.Live -> Hearthstone.Logs, Hearthstone.Protocol, Tracking
IceCrow.Recording -> Battlegrounds, Battlegrounds.Memory,
                     Hearthstone.Entities, Hearthstone.Protocol, Tracking
IceCrow.Tracking -> Battlegrounds, Battlegrounds.Memory,
                    Hearthstone.Entities, Hearthstone.Protocol
IceCrow.Battlegrounds.Memory -> Battlegrounds
IceCrow.Battlegrounds -> Hearthstone.Entities, Hearthstone.Protocol
IceCrow.Hearthstone.Entities -> Hearthstone.Protocol
IceCrow.Hearthstone.Decks -> Hearthstone.Data
IceCrow.Infrastructure.ManacostApi -> Hearthstone.Data
IceCrow.Telemetry -> Tracking
```

`Platform.Windows`, `Hearthstone.Logs`, `Hearthstone.Protocol`, `Hearthstone.Data`, and `ClientState` are leaves. The graph is acyclic. Existing architecture tests enforce exact project references and major forbidden dependencies.

## Runtime flows and state ownership

| Flow | Owner | Mutable state | Published boundary |
| --- | --- | --- | --- |
| Hearthstone window | `OverlayHost` / `Platform.Windows` | native window tracking and overlay lifetime | presentation model rendered by overlay |
| Power log input | `PowerLogTailer` | byte checkpoint, partial line, bounded channel | `RawLogLine` |
| Live tracking | `LiveTrackingCoordinator` / `TrackingSession` | parser context and canonical match state | immutable `LiveTrackingUpdate` / `TrackingSnapshot` |
| Card data | `InMemoryCardDatabase` / synchronizer | atomically replaced frozen database state | data interfaces and immutable records |
| Telemetry | consent + local outbox | opt-in preference and bounded summaries | local file only; no transport exists |
| WPF lifetime | `App` | all objects and tasks above | currently none: orchestration is concentrated in one class |

The tracking core remains deterministic. It does not know about WPF, files, HWNDs, HTTP, telemetry, or optional presentation features. Feature consumers receive immutable tracking updates rather than mutating `TrackingSession`.

## Hot-path observations

| Path | Observation | Decision |
| --- | --- | --- |
| Power parser | 100,000-line diagnostic: 99,957 lines/s | Keep parser structure; no speculative rewrite |
| Tracking reducer | 50,000 events: 467,308 events/s | Preserve lazy snapshot creation and bounded state |
| Replay | 25,002 events: 158,884 events/s | Preserve deterministic, delay-free replay |
| Entity snapshots | 2,000 entities / 8,000 tags: 4.42 ms | Existing immutable snapshot cost is acceptable |
| Card lookup | 100,000 CardId + DBF pairs: 50.2 ms | Frozen indexes provide the required O(1) lookup |
| Data snapshot | 10,000 records: save 352.8 ms, load 160.1 ms, apply 18.4 ms | Cold/background path; do not optimize without evidence |
| Image-cache lookup | 10,000 lookups: 715.5 ms | Bounded cache is not currently wired into the app hot path |

The measurements include JIT, filesystem, and machine noise. The after-state must use the same diagnostics and may not claim improvements below that noise floor.

## Change decisions

### 1. Split the composition root

**Problem:** `App.xaml.cs` is 364 lines and owns log, data, telemetry, overlay, diagnostics, cancellation, and disposal.
**Evidence:** four synchronous task waits occur during disposal; unrelated failure and shutdown policies live in one class.
**Change:** add concrete internal runtime coordinators under `IceCrow.App/Runtime`; keep `App` responsible only for WPF startup, shutdown, and fatal error reporting.
**Expected benefit:** explicit ownership and shutdown order, smaller review surface, feature-independent runtime components.
**Cost:** a few small classes and forwarding callbacks; no new assembly or container.
**Test:** build/tests, architecture tests, and manual WPF startup/shutdown.

### 2. Make normal shutdown asynchronous

**Problem:** synchronous waits can block the dispatcher while file and network tasks unwind.
**Evidence:** `.GetAwaiter().GetResult()` appears four times in `App.Dispose`.
**Change:** one root cancellation token and `IAsyncDisposable` runtimes; the normal window-close path awaits shutdown. WPF's synchronous `OnExit` remains only as an idempotent fallback boundary.
**Expected benefit:** orderly cancellation without multiple independent stop paths.
**Cost:** WPF still exposes a synchronous final callback; this constraint is documented rather than hidden.
**Test:** lifetime unit tests where pure logic exists plus application build and manual close.

### 3. Harden external-input boundaries

**Problem:** the three defects listed in the executive conclusion can cause duplicate processing, avoidable allocation pressure, or inconsistent error handling.
**Evidence:** source review and focused reproductions from the previous security discovery.
**Change:** distinguish truncation/replacement from timestamp-only touches; stream the telemetry array with an early item limit; validate nullable cache envelope members before conversion.
**Expected benefit:** deterministic checkpointing, bounded deserialization, and stable recoverable error taxonomy.
**Cost:** small parsing branches and regression fixtures.
**Test:** focused unit tests followed by the full suite and security diff scan.

### 4. Improve executable architecture documentation

**Problem:** architecture facts are spread across `README`, `AGENTS.md`, tests, and milestone reports.
**Evidence:** no single module-boundary, threading, error, dependency, resource-budget, or feature-development document exists.
**Change:** add concise documents and strengthen architecture tests for newer modules and forbidden source patterns.
**Expected benefit:** both human and AI contributors can choose the correct project and validation path before editing.
**Cost:** documentation must be maintained with graph changes.
**Test:** links, exact graph tests, formatting, and `git diff --check`.

## Explicit non-changes

- No telemetry HTTP transport: the server has no confirmed endpoint and no secret is required or appropriate.
- No deckstring rewrite: the third-party codec remains behind the existing adapter.
- No new production project: the existing graph already expresses the necessary boundaries.
- No broad public-API reduction: 152 public production types were inventoried, but no consumer breakage or maintenance gain justifies a sweeping change.
- No parser or reducer micro-optimization: current diagnostics show no meaningful bottleneck in those paths.

## Risks and validation strategy

The composition-root refactor has application-lifetime risk; the external-input fixes have compatibility risk. Changes must remain behavior-preserving and be validated in this order: focused tests, complete Debug and Release gates, formatting and whitespace checks, same performance diagnostics, security review, then GitHub Actions after push.
