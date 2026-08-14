# Anonymous match telemetry foundation

Telemetry is optional and defaults to off. IceCrow never uploads `Power.log`.

```text
ended TrackingSnapshot
  -> MatchSummaryFactory
  -> consent check
  -> bounded persistent TelemetryOutbox
  -> optional future ITelemetryTransport
  -> acknowledge by random match ID
```

`MatchSummary` schema version 1 contains only fields currently derived with
reliable semantics: random UUIDv7 match ID, game/queue mode, optional patch,
client version, hero card ID, turns, tavern progression, triples, and start/end
timestamps. Placement and rating bucket remain null until a trustworthy source
exists. Exact MMR is not collected.

The outbox lives at `%LOCALAPPDATA%\IceCrow\telemetry\outbox.json`, keeps at
most 128 summaries, uploads batches of at most 25 through an injected
transport, and removes only acknowledged IDs. The preference lives in a
separate small atomic file and defaults to
`ShareAnonymousGameplayStatistics=false` when absent or unreadable.

No server ingest or installation credential was added in this milestone.
Before enabling uploads, the API needs a dedicated bounded schema, rate limits,
idempotency, and a per-installation minimally scoped proof/token design. A
shared desktop bearer token and the Manacost admin token are prohibited.

Consequently, transport-specific behavior such as HTTP 401 exchange, rate
limiting, installation revocation, and server schema rejection is deliberately
not implemented or claimed. The client foundation tests consent, summary
derivation, persistence, deduplication, bounds, partial acknowledgement,
transport failure retention, and retry through the transport abstraction.
