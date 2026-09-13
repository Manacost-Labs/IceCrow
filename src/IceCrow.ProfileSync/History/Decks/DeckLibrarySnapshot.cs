using System.Collections.Immutable;

namespace IceCrow.ProfileSync.History.Decks;

public sealed record DeckVersionStatistics(
    DeckRevisionIdentity Identity,
    int Games,
    int Wins,
    int Losses,
    int Ties,
    int UnknownResults,
    DateTimeOffset? LastPlayedAt,
    Certainty Confidence,
    string? HeroCardId);

public sealed record DeckFamilyStatistics(
    Guid Id,
    string? Name,
    string Format,
    ImmutableArray<DeckVersionStatistics> Versions,
    string CurrentRevisionKey,
    bool IsActive,
    int Games,
    int Wins,
    int Losses,
    int Ties,
    int UnknownResults,
    DateTimeOffset? LastPlayedAt,
    Certainty Confidence,
    string? HeroCardId)
{
    public DeckVersionStatistics CurrentVersion =>
        Versions.First(version => string.Equals(
            version.Identity.Key,
            CurrentRevisionKey,
            StringComparison.Ordinal));
}

public sealed record DeckLibrarySnapshot(
    ImmutableArray<DeckFamilyStatistics> Families,
    string? ActiveRevisionKey,
    string Message,
    bool IsError)
{
    public static readonly DeckLibrarySnapshot Empty = new(
        [],
        null,
        "Каталог колод готов.",
        false);
}

public sealed record DeckLibraryOperationResult(
    bool Success,
    string Message,
    DeckLibrarySnapshot Snapshot);
