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
