# IceCrow.ProfileSync

Personal, authenticated HearthPulse profile synchronization. This boundary
owns the profile record contracts, the durable local outbox, the batched
uploader, device linking, and the credential store contract. It is separate
from `IceCrow.Telemetry` on purpose: telemetry is anonymous and consent-gated,
profile sync is personal and linked to a HearthPulse account.

Rules:

- Records are immutable, bounded, and carry typed `Certainty`. A factory maps
  tracking results into records; never raise a certainty while mapping.
- No HTTP per gameplay event. Completed records go to the outbox; the
  coordinator uploads bounded batches with backoff and jitter, preferably
  outside gameplay.
- Every event has an idempotent `EventId`; match and Arena history must
  never be silently dropped (an outbox overflow is an explicit, counted
  rejection). Collection snapshots are state: the latest pending snapshot
  replaces older unsent ones.
- Credentials are stored only through `IProfileCredentialStore` behind an
  `ISecretProtector`; no plaintext token files, no embedded shared or admin
  token, and never log tokens, raw game handles, or full collection payloads.
- Failure of sync must never affect tracking. Do not reference WPF, Live, or
  Recording.
