using System.Collections.Immutable;

namespace IceCrow.ProfileSync.History.Decks;

internal sealed record DeckFamilyDefinition(
    Guid Id,
    string Name,
    string Format,
    string? HeroCardId,
    ImmutableArray<DeckRevisionIdentity> Revisions);

internal sealed record DeckLibraryCatalog(
    int FormatVersion,
    ImmutableArray<DeckFamilyDefinition> Families,
    string? ActiveRevisionKey)
{
    public const int CurrentFormatVersion = 1;

    public static readonly DeckLibraryCatalog Empty = new(CurrentFormatVersion, [], null);
}
