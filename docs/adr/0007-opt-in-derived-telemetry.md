# ADR 0007: Opt-in derived telemetry with no shared secret

Status: Accepted (2026-08-14)

Telemetry is derived from immutable match state, never raw logs, and defaults
off. A bounded offline outbox exists without a server transport. Any later
desktop authentication must be per installation and minimally scoped; no
shared or master token may ship with IceCrow.
