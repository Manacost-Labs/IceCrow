# Deck library and versioned statistics

IceCrow presents deck history as user-owned **deck families** instead of raw
deck codes. A family is the concept a player recognizes (for example,
"Контроль воин"); an exact revision is one canonical Standard or Wild
deckstring used in one or more matches.

## Product behavior

- Selecting or importing a new code creates an exact revision and makes it the
  active deck for the next match.
- Matches keep the exact revision captured at `CREATE_GAME`. Later UI changes
  never relabel an in-progress or completed match.
- A family shows an overall record across all its revisions and a separate
  record for its current revision.
- The user can select two families of the same format and merge them as
  revisions of one deck. Standard and Wild families cannot be merged.
- Splitting a family restores one family per exact revision. No match is moved,
  copied, or deleted, so this operation is reversible.
- Raw deck codes, hashes, and internal identifiers are not used as primary UI
  labels.

The current shipped source for Standard and Wild deck identity is the explicit
active deck chosen in IceCrow. This association is `Inferred`: `Power.log`
cannot prove which complete deck is selected in the Hearthstone client. The
`IceCrow.Hearthstone.ClientState.ISelectedDeckSource` contract is the future
authoritative input; no unlicensed memory-reading adapter is bundled. Until
that source exists, the UI must not claim automatic client selection.

## Ownership and data flow

```text
active deck import/selection
  -> ActiveDeckRuntime (validated canonical deckstring)
  -> ProfileRecordPipeline snapshots selection at CREATE_GAME
  -> immutable HistoryMatch / HistoryDeck records
  -> DeckLibraryProjector
       exact revisions + reversible family metadata
  -> DeckLibrarySnapshot
  -> HistoryWindow deck workspace
```

`IceCrow.ProfileSync.History.Decks` owns family metadata because this is
permanent personal history organization. It does not depend on WPF, live
tracking, the parser, or HearthPulse. `IceCrow.App` only maps immutable
snapshots to controls and forwards user operations.

The match journal remains the source of statistical facts. The catalog at
`%LOCALAPPDATA%\IceCrow\decks\catalog.json` contains only names, exact revision
identities, the active revision, and optional hero metadata. The projector
joins both immutable inputs in memory. This prevents UI grouping from changing
historical evidence or upload payloads.

## Performance and safety

Projection is bounded by the existing 4,096-match history plus a catalog of at
most 256 families and 1,024 revisions. Hero resolution indexes match history in
one pass; family aggregation then processes every revision once. UI rows are
rebuilt only when history, card data, or the deck-library snapshot changes.

Catalog reads are limited to 1 MiB and validated before publication. Names,
formats, revision counts, identifiers, and deckstring sizes are bounded. Writes
use a temporary file followed by an atomic same-directory replacement. A bad
catalog produces an explicit error and an empty grouping layer; immutable match
history remains available and unchanged.

## Next slices

1. Connect a licensed `ISelectedDeckSource` implementation and register exact
   client selections automatically.
2. Add card-list comparison and a clear revision diff (added/removed cards).
3. Add optional labels and archive state without hiding match history.
4. Add per-family matchup and time-range analytics once opponent archetype
   evidence has a typed certainty model.
