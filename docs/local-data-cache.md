# Offline local data cache

## Decision

IceCrow uses a versioned JSON snapshot rather than SQLite for the first data
layer. The current workload is a bounded, infrequently replaced card/hero
catalog with exact CardId/DBF lookups and simple in-memory BG filters. Frozen
dictionaries make those lookups constant-time, while a complete immutable
replacement keeps readers lock-free.

SQLite becomes justified when full-text search, substantially larger
constructed catalogs, or multi-table relationships cannot be served
comfortably by the measured snapshot.

## Guarantees

- cache schema version, data version, optional Hearthstone build, creation time,
  and SHA-256 are stored with the snapshot;
- startup begins without waiting for network and loads the last-known-good file;
- updates are written to a same-directory temporary file with write-through,
  reopened, deserialized, bounded, and hash-validated before replacement;
- a timeout, 500, malformed response, unsupported schema, corruption, or hash
  mismatch leaves the previous file and in-memory database untouched;
- no cache plus no internet produces an empty database and unknown metadata,
  not a tracking failure.

The current location is `%LOCALAPPDATA%\IceCrow\data\hearthstone-data.json`.
The snapshot is capped at 64 MiB, 20,000 cards, and 1,000 heroes.

Images use a separate async disk cache foundation. URLs must use HTTPS,
the composition root must explicitly allow each trusted image host,
individual files are capped at 8 MiB, concurrent requests are deduplicated,
failures are negative-cached for five minutes, and total storage defaults to
256 MiB. WPF never performs synchronous downloads.

## Local performance baseline

Measured on the development Windows machine on 2026-08-14 using the checked-in
non-threshold diagnostic test (10,000 synthetic records): snapshot save 340.8
ms, cold load 155.3 ms, atomic in-memory apply 28.7 ms, 100,000 paired CardId
and DBF lookups 60.6 ms, one BG filter query 10.1 ms, and 10,000 disk-cache
existence lookups 870.2 ms. These figures are observations, not CI limits;
machine and storage differences are expected.
