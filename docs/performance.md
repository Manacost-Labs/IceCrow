# Performance evidence

Performance checks are diagnostic and deliberately not CI timing thresholds.
Run them on the same machine/configuration and look for order-of-magnitude
regressions, allocation growth, or a change in algorithmic shape.

```powershell
dotnet run --project tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj -- `
  benchmark --repository-root .
dotnet test tests/IceCrow.Infrastructure.ManacostApi.Tests/IceCrow.Infrastructure.ManacostApi.Tests.csproj `
  --filter "FullyQualifiedName~DataPerformanceBaselineTests"
```

## Before refactoring (2026-08-14)

| Diagnostic | Result |
| --- | --- |
| Power parser | 100,000 lines, 1,000.43 ms, 99,957 lines/s |
| `TrackingSession` | 50,000 events, 107.00 ms, 467,308 events/s |
| `ReplayRunner` | 25,002 events, 157.36 ms, 158,884 events/s |
| Entity snapshots | 2,000 entities / 8,000 tags, 4.42 ms |
| Synthetic golden fixture | 178.10 ms |
| Data save/load/apply | 10,000 records, 352.8 / 160.1 / 18.4 ms |
| CardId + DBF lookup | 100,000 pairs, 50.2 ms |
| Data filter | 6.9 ms |
| Image cache lookup | 10,000 lookups, 715.5 ms |

## After refactoring

Four fresh process runs were used; the table reports the median (average of the
two middle values for an even sample). Allocation values were stable across the
runs.

| Diagnostic | Result |
| --- | --- |
| Power parser | 93,969 lines/s, 1,274 B/line |
| `TrackingSession` | 358,157 events/s, 792 B/event |
| `ReplayRunner` | 254,888 events/s, 792 B/event |
| Entity snapshots | 2,000 entities / 8,000 tags, 6.24 ms, 1,360,448 B |
| Synthetic golden fixture | 182.11 ms, about 794 KiB |

The parser/tracking/replay/entity source files did not change in this milestone,
so no hot-path allocation change is attributable to the refactoring. Allocation
instrumentation was added after the initial timing run; it establishes the new
repeatable baseline rather than inventing a before value. Timing moved in both
directions (parser about -6%, tracking about -23%, replay about +60%, fixture
about +2% versus single-run before values), which demonstrates why these
diagnostics are not thresholds. No algorithmic path or retained-state budget
changed, and the full quality/soak evidence remains the regression authority.

## Overlay rendering

Overlay cost has its own budget, rules, and manual procedure. See
[UI performance rules](ui-performance-rules.md) and the
[UI performance test plan](ui-performance-test-plan.md). The short version: the
overlay has no render loop, no runtime blur, at most one small shadow per
floating panel, only finite animations, and it drops a view-state update whose
value did not change. `OverlayRenderDiagnostics` exposes bounded counters in the
Debug developer window. Idle CPU and steady-state memory still have no
trustworthy baseline; capture them during the real-client acceptance run.

## Hot-path policy

- Parse once into normalized events; never re-read a multi-megabyte log on each
  update.
- Keep canonical state single-writer and snapshots lazy/material-change driven.
- Avoid sorting or scanning all retained state on each event; maintain bounded
  counters/indexes when diagnostics need maxima.
- UI is latest-only and must not make the tracking consumer wait for dispatcher
  rendering, card images, data refresh, telemetry, or network access.
- Cold startup paths are optimized only after profiles show user-visible cost.

## Initial regression goals

These are comparison rules derived from the measured implementation, not
marketing targets:

- Investigate a reproducible median parser/tracking throughput drop greater than
  30% on the same machine and fixture. Real Hearthstone input is far below the
  measured 90,000+ lines/s, so correctness and bounded memory remain primary.
- Investigate a reproducible hot-path allocation increase greater than 25% per
  line/event when behavior and fixture size are unchanged.
- Preserve O(1) indexed CardId/DBF lookup; investigate if the 100,000-pair
  diagnostic exceeds twice its established same-machine baseline.
- Preserve latest-only overlay dispatch. Rendering should never queue one WPF
  operation per log event; visual frame-rate validation remains manual.
- Keep optional 10,000-record cache loading off the tracking/UI hot path. A
  same-machine regression above twice the baseline needs a profile.
- Idle CPU, steady-state private memory, handle count, and mixed-DPI overlay
  update cadence do not yet have trustworthy baselines. Do not invent numeric
  limits; capture them during the real-client acceptance/soak run first.

Small timing changes are noise. Any intentional algorithm, allocation, or
budget regression must include the measurement and user-visible justification.

## Headless companion (2026-09-06)

Measured with `tools/perf/Measure-IdleProcess.ps1` (Release build, this
machine: 8 logical cores, Windows 11 26100) while the Hearthstone client was
open at the menu with a 108 MiB stale `Power.log` from an earlier session.
`dotnet-counters` sampled `System.Runtime` once per second; process CPU is
`TotalProcessorTime` over the window divided by all eight cores. Numbers are
same-machine evidence, not CI thresholds.

| Run (30 s window after 10 s warm-up) | Overlay | Process avg CPU (8 cores) | Working set | Private | Steady alloc | Steady CPU (one core) | Exceptions | IceCrow modules loaded |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Baseline `806ebb1` | on | 5.9 % | 163 MiB | 100 MiB | 90 KiB/s | 3.4 % | 0.96/s | 15 (incl. Overlay, Presentation) |
| Headless default (`460731a`) | off | 2.4 % | 125 MiB | 69 MiB | 77 KiB/s | 2.2 % | 0.93/s | 14 (no Overlay, no Presentation) |
| + idle tailer fixes (`dc9c8dd`) | off | 3.1 % | 125 MiB | 69 MiB | 48 KiB/s | 1.5 % | 0 | 14 |

"Steady" excludes the start-up burst (seconds whose allocation rate exceeded
5 MiB/s). Every process average is dominated by that burst: on a cold start
the tailer replays the whole existing `Power.log` from offset 0 so an
in-progress match is never missed, and a 108 MiB stale log costs roughly
1.7 GiB of transient allocations and about 10 s of CPU in the baseline run
(spread over one to six seconds of the window depending on where the warm-up
cut). The 60 s steady-state run below starts after that burst.

| Run (60 s window after 45 s warm-up) | Overlay | Process avg CPU (8 cores) | Working set | Private | Alloc | Gen0 / Gen1 / Gen2 | Exceptions | Threads |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Headless steady state at the menu (`dc9c8dd`) | off | 0.125 % (≈ 1 % of one core) | 120 MiB (max 121) | 65 MiB | 51 KiB/s | 0 / 1 / 0 | 4 in 60 s | 27 |

Findings:

- Disabling the overlay removes the 33 ms modifier-polling and 1 s window
  timers, ~38 MiB of working set, and two assemblies; the Release default is
  now headless (`docs/profile-sync.md`).
- The tailer raised one `OperationCanceledException` per recovery tick
  (`dotnet.exceptions` ≈ 1/s at idle) and re-ran the full Power.log locate
  (process enumeration plus a session-directory scan) every second; both are
  gone (`6b7413b`, `dc9c8dd`).
- Menu idle after the fixes is well inside the `< 0.5 % average CPU` target
  once the cold-start replay is over; the replay of a large stale log is the
  remaining start-up cost and is recorded as a residual, not hidden inside
  the averages.
- Working set ~125 MiB at the menu is within the 100–120 MiB aim only after
  the burst-inflated GC heap is trimmed; private bytes sit at ~69 MiB. Menu,
  Constructed, Arena, and Battlegrounds match scenarios with the client in
  those states still need a live run (see the acceptance checklist).

## Settled decisions

- **Board-diff allocations (2026-08-16).** The ambiguity-safe duplicate-group
  matcher allocates slightly more than the original naive matcher. This is
  accepted: the comparison stays microsecond-scale over at most seven minions
  per side and only runs when an opponent board is re-observed. Do not add
  caching or pooling here unless a profiled real session shows user-visible
  cost; correctness of identity confidence beats a few hundred bytes.
