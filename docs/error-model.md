# Error model

IceCrow separates external conditions from programming faults. Boundary code
may translate exceptions; domain code should not catch `Exception` to continue
with unknown state.

| Category | Examples | Required behavior |
| --- | --- | --- |
| Expected absence | Hearthstone/log/cache not present | return empty/not-found state or wait with bounded recovery |
| Recoverable external failure | sharing violation, offline API, invalid optional cache | retain last-known-good state, publish bounded diagnostic |
| Invalid untrusted input | malformed Power line, replay JSON, cache envelope | ignore explicitly or throw `InvalidDataException`/typed parse result |
| Safety-limit rejection | entity/tag/replay work budget exceeded | typed limit exception or counted rejection; never grow past the cap |
| Cancellation | root shutdown or caller cancellation | rethrow/finish only when the requested token is cancelled |
| Programming fault | invariant violation, impossible discriminator | fail visibly; do not silently downgrade to external noise |

## Translation boundaries

- `PowerLineParser` returns parsed, ignored, malformed, or unknown results.
- `LiveTrackingCoordinator` converts typed tracking-limit exceptions into a
  bounded rejection counter because the next log line may still be valid.
- Recording, telemetry, and data-cache readers translate malformed JSON into
  `InvalidDataException`; required `null` values use the same contract.
- Manacost transport errors become an offline status while the last-known-good
  database remains authoritative for optional enrichment.
- WPF/runtime callbacks write concise diagnostics and do not expose raw logs or
  credentials.

Do not log the same malformed pattern per input line. Counters and sampled
details are preferred on parser hot paths.
