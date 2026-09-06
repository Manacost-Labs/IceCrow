# Privacy and data collection

## Default behavior

Anonymous gameplay sharing is **off by default**. With consent off, summaries
are not added to the telemetry outbox and no telemetry transport is called.
Static public card-data synchronization is independent of telemetry and sends
no API token.

## Potentially collected after explicit consent

- random UUIDv7 match ID generated independently of account or machine IDs;
- game and queue mode;
- optional Hearthstone patch and IceCrow client version;
- hero CardId, turns, tavern progression, and triples;
- start and end timestamps;
- future coarse rating bucket only after separate privacy approval.

## Never collected by this foundation

- raw `Power.log` or replay files;
- BattleTag, email, account ID, passwords, chat, machine username, or absolute
  local paths;
- exact MMR;
- installation private keys, access tokens, or Authorization headers.

Installation identity, if introduced, is for authentication, rate limiting,
and deduplication and should be stripped before analytics where practical.

## Personal profile sync (linked device)

Profile sync is a separate, personal boundary (`IceCrow.ProfileSync`,
`docs/profile-sync.md`). It is inert until the user explicitly links the
device to their HearthPulse account through the OAuth device flow
(`--link-hearthpulse`); the device credential is revocable, least-privilege,
and stored only behind Windows DPAPI. After linking, the following is synced
to the user's own profile in bounded, idempotent batches: ranked
Standard/Wild and Arena match results with turns, duration, hero card ids,
the user's own mulligan, the count of opponent mulligan replacements, and
opponent card ids actually observed; Battlegrounds mode, hero, placement,
duration, final turn, and the user's own final board; the user's collection
counts by card id (hash-deduplicated, latest state only). Player names,
BattleTags, account ids, raw `Power.log`, hidden opponent cards, and
server game handles are never stored or uploaded. `--unlink-hearthpulse`
revokes the credential and clears it locally; pending events stay local and
are never uploaded without a credential.
