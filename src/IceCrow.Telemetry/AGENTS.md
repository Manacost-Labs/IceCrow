# Telemetry boundary

Consent is off by default and must be checked at the persistence/transport
boundary. Store only bounded derived match summaries: never raw Power lines,
player identity, credentials, or arbitrary runtime type metadata. The local
outbox is bounded and valid without a backend. Do not implement or enable a
transport until a concrete authenticated server contract exists; network
failure must never affect tracking.
