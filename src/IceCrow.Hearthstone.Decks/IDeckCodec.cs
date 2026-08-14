namespace IceCrow.Hearthstone.Decks;

public interface IDeckCodec
{
    DeckDecodeResult Decode(string value);

    string Encode(DeckDefinition deck);

    DeckValidationResult Validate(DeckDefinition deck);

    DeckDefinition Canonicalize(DeckDefinition deck);

    DeckExportParseResult ParseExport(string clipboardText);

    string FormatExport(DeckDefinition deck, DeckExportMetadata? metadata = null);
}
