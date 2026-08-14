using IceCrow.Hearthstone.Data;
using Package = ManacostLabs.Deckstrings;

namespace IceCrow.Hearthstone.Decks;

public sealed class ManacostDeckCodec : IDeckCodec
{
    public const string PackageVersion = "1.0.0";
    private readonly ICardDatabase? _cardDatabase;

    public ManacostDeckCodec(ICardDatabase? cardDatabase = null)
    {
        _cardDatabase = cardDatabase;
    }

    public DeckDecodeResult Decode(string value)
    {
        try
        {
            return new DeckDecodeResult(true, FromPackage(Package.Deckstrings.Decode(value)), null, null);
        }
        catch (Package.DeckstringException exception)
        {
            return new DeckDecodeResult(false, null, exception.ErrorCode, exception.Message);
        }
    }

    public string Encode(DeckDefinition deck) => Package.Deckstrings.Encode(ToPackage(deck));

    public DeckValidationResult Validate(DeckDefinition deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        var result = Package.Deckstrings.Validate(ToPackage(deck));
        return new DeckValidationResult(
            result.IsValid,
            result.Errors.Select(error => new DeckValidationError(error.Code, error.Path, error.Message)).ToArray());
    }

    public DeckDefinition Canonicalize(DeckDefinition deck) =>
        FromPackage(Package.Deckstrings.Canonicalize(ToPackage(deck)));

    public DeckExportParseResult ParseExport(string clipboardText)
    {
        try
        {
            var parsed = Package.Deckstrings.ParseExport(clipboardText);
            return new DeckExportParseResult(
                true,
                new DeckExport(
                    FromPackage(parsed.Deck),
                    parsed.Deckstring,
                    FromPackage(parsed.Metadata)),
                null,
                null);
        }
        catch (Package.DeckstringException exception)
        {
            return new DeckExportParseResult(false, null, exception.ErrorCode, exception.Message);
        }
    }

    public string FormatExport(DeckDefinition deck, DeckExportMetadata? metadata = null)
    {
        var packageMetadata = new Package.DeckExportMetadata { Name = metadata?.Name };
        foreach (var comment in metadata?.Comments ?? [])
        {
            packageMetadata.Comments.Add(comment);
        }

        return Package.Deckstrings.FormatExport(
            ToPackage(deck),
            packageMetadata,
            dbfId =>
            {
                var card = _cardDatabase?.GetByDbfId(dbfId);
                return card is null ? null : new Package.CardDisplay(card.Name, card.Cost);
            });
    }

    private static Package.Deck ToPackage(DeckDefinition deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        var result = new Package.Deck { Format = (Package.DeckFormat)deck.Format };
        foreach (var hero in deck.Heroes)
        {
            result.Heroes.Add(hero);
        }

        foreach (var card in deck.Cards)
        {
            result.Cards.Add(new Package.DeckCard(card.DbfId, card.Count));
        }

        foreach (var card in deck.SideboardCards)
        {
            result.SideboardCards.Add(new Package.SideboardCard(card.DbfId, card.Count, card.OwnerDbfId));
        }

        return result;
    }

    private static DeckDefinition FromPackage(Package.Deck deck) => new(
        (DeckFormat)deck.Format,
        deck.Heroes,
        deck.Cards.Select(card => new DeckCard(card.DbfId, card.Count)),
        deck.SideboardCards.Select(card => new DeckSideboardCard(card.DbfId, card.Count, card.OwnerDbfId)));

    private static DeckExportMetadata FromPackage(Package.DeckExportMetadata metadata) =>
        new(metadata.Name, Array.AsReadOnly(metadata.Comments.ToArray()));
}
