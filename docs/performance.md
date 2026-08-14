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
