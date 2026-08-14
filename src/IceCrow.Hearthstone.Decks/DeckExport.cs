namespace IceCrow.Hearthstone.Decks;

public sealed record DeckExport(DeckDefinition Deck, string Deckstring, DeckExportMetadata Metadata);
