# Resource budgets

All values are hard defaults unless described as warnings. Increasing a limit
requires a corpus or soak result and a security review.

| Surface | Warning / default | Hard maximum / policy |
| --- | --- | --- |
| Power log line | — | 64 KiB bytes; oversized line discarded |
| Power log channel | 128 lines | configurable no higher than 256; producer waits |
| Power log rewrite fingerprint | — | 64 KiB suffix; verification work is independent of total log size |
| `log.config` | — | 1 MiB |
| Parser input | — | 256 Ki characters |
| Power block nesting | — | 128 |
| Remembered malformed patterns | — | 64 |
| Live pending events | 512 | 4,096; oldest evicted and counted |
| Lifecycle player identities | 192 | 256 |
| Tracked entities | 8,192 | 32,768 |
| Tags per entity | 128 | 256 |
| Total entity tags | 250,000 | 1,000,000 |
| Lobby players | 12 | 16 |
| Timeline events/player | 384 | 512 |
| Opponent snapshots/player | 96 | 128 |
| Recording file | — | 64 MiB |
| Recording retained estimate | — | 32 MiB |
| Recording events/checkpoints | — | 100,000 / 4,096 |
| Recording string | — | 256 Ki characters |
| Private capture store | — | 24 captures, 256 MiB total; oldest pruned first |
| Private capture temp files | — | removed before the first save of a process |
| Replay entities/opponent snapshots | — | 4,096 / 64 |
| Replay materialization work | — | 10,000,000 work units |
| Telemetry runtime queue | 16 | oldest optional summary dropped |
| Telemetry outbox | — | 128 summaries, 8 MiB; streamed read stops at item 129 |
| Manacost response | 8 MiB | 100 pages; validated DTO string/count limits |
| Hearthstone data cache | — | 64 MiB, content hash and identity uniqueness |
| Card image | — | 8 MiB each; disk cache has configured total cap |

Queues cap retained work, not just file size. New external arrays must be
streamed or preflighted before materialization where the wire/file format allows
it. Diagnostic calculations must remain O(1) or bounded by the same caps.

Private capture bounds are provisional pending measured real-match recording
sizes; the store refuses a total-byte limit below one maximum-size recording so
retention can never truncate a normal match. Release composes a null capture
observer, so its hot path pays exactly one null check per notification point.
In Debug the observer is attached: capture disabled costs one interface call
plus an uncontended lock per applied event, and capture enabled adds recorder
validation per event. Completed matches queue into a bounded two-slot channel
saved by one sequential worker off the hot path; a full queue drops the
capture with an explicit error instead of blocking tracking.
