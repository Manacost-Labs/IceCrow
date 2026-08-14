# IceCrow M15 Hardening Report

Date: 2026-08-14
Scope: parser fuzzing, replay mutation, log-tailer races, long-run tracking, shutdown, live-state resource limits, and diagnostic performance baselines.

## Outcome

Milestone 15 adds deterministic hostile-input coverage and explicit live-state policies without adding a user-facing feature or a third-party fuzzing package. Fast property/mutation/race tests remain in the normal quality gate. Longer lifetime tests use `Category=Soak` and are available through a manual GitHub Actions job.

The current evidence supports the narrower claim that malformed inputs and sustained synthetic streams are handled within explicit logical bounds. It does not establish production readiness: native hook/WPF lifetime behavior and real Hearthstone log behavior still require the manual acceptance matrix.

## Test categories

| Category | Coverage | Normal CI |
| --- | --- | --- |
| Parser property tests | Random ASCII and Unicode, invalid surrogate characters, long tokens, partial/corrupt records, delimiters, numeric extremes, and unbalanced/deep blocks | Yes |
| Replay mutation tests | Event reordering, required-field removal, unknown discriminator, huge string, numeric enum, invalid checkpoint, `$type`, and truncated JSON | Yes |
| Log-tailer races | Rapid append, delete/recreate, truncate/rewrite, rotation, cancellation under backpressure, and repeated watcher disposal | Fast races: yes; repeated shutdown: soak |
| Tracking soak | 5,000 synthetic Battlegrounds turns through parser, lifecycle detection, `TrackingSession`, reducers, opponent memory, and lobby timeline | Manual soak job |
| Shutdown soak | 200 coordinator start/cancel cycles and 75 tailer/watch-directory start/read/cancel/dispose cycles | Manual soak job |
| Performance baseline | Parser, tracking, replay, entity snapshots, and full fixture replay | Developer command only; never a timing gate |

The parser property suite uses fixed seed `0x1CEC0DE` (`30,327,006`) and includes 2,000 generated cases plus fixed adversarial cases. A failure message includes the seed, case index, and input length so it can be reproduced.

## Resource limits

Warnings are diagnostics; reaching one does not reject input. Hard limits prevent further growth in that dimension and are surfaced through live diagnostics. Replay uses a separate, stricter policy because a supplied file can be rejected deterministically, while a legitimate long live match should retain more headroom.

### Live tracking

| Resource | Warning | Hard limit | Behavior at hard limit |
| --- | ---: | ---: | --- |
| Tracked entities | 8,192 | 32,768 | Reject only a new entity mutation; keep the active session usable |
| Tags on one entity | 128 | 256 | Reject only a new tag; existing tags remain updateable |
| Total retained entity tags | 250,000 | 1,000,000 | Reject only a new tag and do not partially create its entity |
| Lobby players | 12 | 16 | Reject only a new lobby player |
| Timeline events per player | 384 | 512 | Retain a bounded newest-event window |
| Opponent snapshots per player | 96 | 128 | Retain a bounded newest-snapshot window |
| Lifecycle player identities | 192 | 256 | Evict the oldest cached identity and report the eviction |

Additional live boundaries:

- parser input: 256 Ki characters per call;
- parser block nesting: 128 levels;
- malformed-pattern diagnostic cache: 64 patterns;
- Power.log line: 64 KiB;
- accepted-line channel: 128 items by default, at most 256, with producer backpressure;
- pre-lifecycle normalized-event buffer: 512 items by default, at most 4,096, dropping oldest with a counter.

### Replay and recordings

- file size: 64 MiB;
- retained materialized strings/events: estimated 32 MiB;
- events: 100,000;
- checkpoints: 4,096;
- one string: 256 Ki characters;
- replay entities: 4,096;
- lobby players: 16;
- board minions: 7;
- opponent snapshots: 64;
- snapshot, per-event snapshot, timeline, and total materialization work have independent cumulative budgets.

Unsupported format versions, unsafe runtime type metadata, invalid discriminators, missing required fields, excessive strings/counts, and truncated JSON fail with bounded `InvalidDataException` paths.

## Soak observations

One Debug diagnostic run processed 20,360 applied events across 5,000 synthetic turns and seven rotating opponents in about 7.53 seconds. Timing and managed-memory observations are intentionally not assertions.

| Observation | Value |
| --- | ---: |
| Managed memory before forced collection | 785,024 bytes |
| Managed memory after forced collection | 8,143,240 bytes |
| Entities | 58 |
| Tags | 313 |
| Timeline events retained | 3,585 |
| Opponent snapshots retained | 896 |
| Maximum timeline events on one player | 512 |
| Maximum opponent snapshots for one player | 128 |
| Safety-limit rejections | 0 |

The logical maxima match the configured ring/window policies rather than the 5,000-turn input length. The same suite also completed 200 coordinator cancellation/restart cycles and 75 tailer watcher lifetime cycles without a channel deadlock or a retained watch-directory handle.

## Performance baseline

Release build, local Windows machine, 2026-08-14. These are developer diagnostics, not cross-machine claims or CI thresholds.

| Operation | Workload | Observed baseline |
| --- | ---: | ---: |
| Power parser | 100,000 lines | 146,989 lines/s; 680.32 ms |
| `TrackingSession` | 50,000 events | 728,369 events/s; 68.65 ms |
| `ReplayRunner` | 25,002 events | 400,329 events/s; 62.45 ms |
| Entity snapshots | 2,000 entities / 8,000 tags | 2.80 ms |
| Full `synthetic-basic-solo` fixture | complete golden replay | 111.84 ms |

Repeat the baseline without rebuilding:

```powershell
dotnet run --project tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj `
  -c Release --no-build -- benchmark --repository .
```

## Running the suites

Fast pull-request tests:

```powershell
dotnet test IceCrow.sln -c Release --no-build --no-restore `
  --filter "Category!=Soak"
```

Independent soak suite:

```powershell
dotnet test IceCrow.sln -c Release --no-build --no-restore `
  --filter "Category=Soak" `
  --logger "console;verbosity=normal"
```

GitHub Actions exposes `workflow_dispatch` input `run_soak`. Normal pushes and pull requests run only fast tests; the optional Windows soak job restores/builds Release and runs the soak category.

## Remaining untested native behavior

- Repeated WinEventHook installation/disposal against a real Hearthstone HWND and its owning thread is not safely reproducible in a headless unit test.
- Full WPF process shutdown, dispatcher teardown, click-through fail-safe, and overlay focus behavior still need the real-client manual matrix.
- Move/resize/minimize/restore, mixed-DPI monitor transitions, ALT interaction, alt-tab, Hearthstone restart, and live log rotation remain manual acceptance items.
- A reliable cross-machine temporary `AccessDenied` race cannot be manufactured without changing ACLs or introducing environment-specific behavior; production recovery paths remain covered indirectly by missing/recreated-file and cancellation tests.
- A same-path replacement that preserves the already-consumed 4 KiB prefix, regrows beyond the old offset, and presents indistinguishable timestamps cannot be distinguished from a normal append without a platform-specific file identity. The tailer now combines `FileShare.Delete`, length/timestamp checks, and a SHA-256-derived fingerprint of up to 4 KiB of consumed prefix data, which covers common truncate/rewrite and rename-rotation cases without rereading the log.

## Security review focus

The focused review covers parser termination/allocation behavior, bounded tailer queues and partial lines, replay deserialization/type controls, and retained live-state cardinality. The M15 changes specifically close the previously identified unbounded entity/tag, timeline, opponent-history, lifecycle-identity, and replay-timeline work paths while preserving higher live limits and warning thresholds for long legitimate games.

Real recordings remain untrusted and private until the fixture tool anonymizes them and a human completes the privacy checklist. The repository still contains only synthetic corpus data.
