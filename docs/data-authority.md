# Data authority

IceCrow separates observed match history from static enrichment. A remote data
failure can reduce names or images to unknown values, but cannot change a
tracked match.

| Data | Authority | Permitted use |
| --- | --- | --- |
| Historical gameplay events | Hearthstone `Power.log` | Normalized by `Hearthstone.Protocol`; never rewritten by remote data. |
| Reconstructed live match | `TrackingSession` | Authoritative mutable match state exposed as immutable snapshots. |
| Last observed opponent board | `OpponentMemory` | Historical evidence captured from tracking entities. |
| Current Hearthstone UI enrichment | `Hearthstone.ClientState` | Supplemental current UI only; never historical truth. |
| Normalized card/meta/statistical data | Manacost API | Optional enrichment and future analytics inputs. |
| Last-known-good normalized data | IceCrow local data cache | Offline source for card and Battlegrounds definitions. |
| Deckstring wire format | `ManacostLabs.Deckstrings` | Canonical encode/decode/validation/export behavior. |
| Backend upstream/reference | hsdata / HearthstoneJSON | Server-side reconciliation only; never an IceCrow runtime dependency. |

`TrackingSession`, protocol parsing, reducers, recording, and replay do not
reference the Manacost HTTP adapter. Card definitions contain static metadata;
live attack, damage, and health remain on tracked entities.
