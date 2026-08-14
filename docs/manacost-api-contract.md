# Manacost API contract used by IceCrow

Inspected against `Manacost-Labs/api.kolodahearthstone.com` revision
`74e38ff3eee52c4b699868fa3dcad53cac0eb86b` on 2026-08-14. The README was
cross-checked against the FastAPI and PHP route implementations.

| Endpoint | Purpose | Auth | Pagination / cache | Offline behavior |
| --- | --- | --- | --- | --- |
| `GET /api/v1/cards?page=N&per_page=200` | Normalized Battlegrounds cards, identities, pool/tier/type and image metadata | Public; IceCrow sends no token | Page size capped by server at 200; `ETag`, `Last-Modified`, public max-age 300 | Existing cache stays active |
| `GET /api/v1/heroes?page=N&per_page=200` | BG heroes, hero powers, buddies and image metadata | Public; IceCrow sends no token | Same pagination and HTTP cache headers | Existing cache stays active |
| `GET /api/v1/cards/{card_id}` | Diagnostic/single lookup, not used during normal sync | Public | Cacheable single record | Local lookup is used instead |
| `GET /api/v1/cards/by-dbf/{dbf}` | Diagnostic/single lookup, not used during normal sync | Public | Cacheable single record | Local lookup is used instead |
| `GET /v1/datasets` | Source publication inventory | Public | Dataset summaries, not a complete IceCrow card snapshot | Not required at startup |
| `GET /v1/health` | Service health | Public | Status/freshness summary | Not required for tracking or cache reads |

The existing dataset inventory does not provide a combined IceCrow manifest
with hashes and client-build metadata. This milestone therefore reuses the
canonical paginated card/hero contracts, derives `dataVersion` from the newest
record `updated_at`, and computes a SHA-256 over the normalized local snapshot.
No duplicate `/v1/client/manifest` endpoint was added.

The client enforces HTTPS, cancellation, a 15-second timeout, 8 MiB per
response, 100 pages per collection, JSON depth 32, record and string limits,
and bounded retries only for 408, 429, 502, 503, and 504. It never retries
400/401/403/404 or incompatible JSON.
