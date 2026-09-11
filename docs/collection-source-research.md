# User collection source research

Date: 2026-09-11

## Result

IceCrow can now ingest a real owned-card collection through the complete JSON
snapshot produced by the
[`Zulut30/HdtCollectionExporter`](https://github.com/Zulut30/HdtCollectionExporter)
HDT plugin. The bridge is clean-room and file based: IceCrow does not load,
download, redistribute, or call HearthMirror.

The snapshot is exact as of its `exportedAt` timestamp. It is not evidence that
the collection is still current after packs, crafting, disenchanting, or a
client restart. Users must create a new export and refresh IceCrow to update it.

## Sources inspected

- Hearthstone Deck Tracker revision
  [`e8732a07887fe117c286d3e4b29642be0487d802`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/e8732a07887fe117c286d3e4b29642be0487d802)
  (2026-09-04). `CollectionHelper.cs` obtains the full collection through
  `Reflection.Client.GetFullCollection()`.
- HDT's bootstrap downloads `HearthMirror.x64.zip`, while HDT's repository is
  marked All Rights Reserved and does not publish a separate HearthMirror
  redistribution licence. Direct integration therefore remains blocked.
- Blizzard's official
  [Hearthstone Game Data APIs](https://develop.battle.net/documentation/hearthstone/game-data-apis)
  expose static game data such as cards and metadata, not a player's owned
  collection.
- Manacost HDT Collection Exporter revision
  [`ec17ccedbbab2c148633033e32b35d970ba32e00`](https://github.com/Zulut30/HdtCollectionExporter/tree/ec17ccedbbab2c148633033e32b35d970ba32e00)
  (2026-05-22). Its complete export schema v3 contains `exportedAt`, `version`,
  and `cards` with normal, golden, signature, and diamond counts.

## Implemented boundary

```text
HDT + Manacost Collection Exporter
  -> complete schema-v3 JSON file
  -> HdtCollectionExportLocator
  -> HdtCollectionExportSource (untrusted-input validation)
  -> CollectionSnapshot
  -> CollectionSyncCoordinator (canonical hash and deduplication)
  -> latest-only ProfileOutbox record
  -> authenticated HearthPulse upload
```

Only `cardId` and owned finish counts cross the importer boundary. IceCrow
deliberately ignores BattleTag, account identifiers, dust, card backs, class
statistics, trial counts, and every unknown property.

Auto-discovery checks only these documented locations:

- `%APPDATA%\HearthstoneDeckTracker\HdtCollectionExporter\last-collection-export.json`;
- the legacy `HdtCollectionExporterRu` equivalent;
- the newest complete `hearthstone-collection-*.json` in
  `%USERPROFILE%\Documents\HDT Collection Exports`.

Delta files containing `-changes-` are rejected because they are not a complete
collection. An explicit file can be selected with `--import-collection`; the
documented locations can be retried with `--refresh-collection`.

## Trust and resource limits

- only format version 3 is accepted;
- maximum input file size: 16 MiB;
- maximum JSON depth: 16;
- maximum cards: 20,000;
- card identifiers and finish counts use the existing client-state bounds;
- duplicate card identifiers, invalid timestamps, invalid JSON, and partial or
  unsupported documents are rejected;
- files are opened read-only with `FileShare.ReadWrite | FileShare.Delete`;
- the source is read once at startup or on an explicit command, never polled;
- personal metadata and collection contents are never written to diagnostics.

## Remaining limitation

Fully automatic, independent collection reads directly from the Hearthstone
client still require a separately licensed and supported client-state adapter.
Until that exists, the HDT exporter is an explicit user-controlled dependency.
IceCrow does not install or update that plugin automatically.
