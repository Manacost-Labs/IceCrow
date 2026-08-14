# ADR 0005: Offline Manacost data boundary

Status: Accepted (2026-08-14)

Manacost API is the remote authority for normalized metadata, while a
hash-validated JSON last-known-good snapshot is the client runtime authority.
The HTTP/cache adapter is a separate assembly and may fail without affecting
tracking. SQLite is deferred until measured query needs justify it.
