using System.Collections.Immutable;
using System.Security.Cryptography;

namespace IceCrow.ProfileSync.History.Decks;

internal static class DeckLibraryProjector
{
    public static DeckLibrarySnapshot Create(
        ProfileHistorySnapshot history,
        DeckLibraryCatalog catalog,
        string message,
        bool isError)
    {
        var latestHeroes = LatestHeroesByRevision(history.Matches);
        var observed = history.Decks
            .Select(deck => CreateVersion(deck, latestHeroes))
            .Where(static version => version is not null)
            .Cast<DeckVersionStatistics>()
            .ToDictionary(static version => version.Identity.Key, StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var families = ImmutableArray.CreateBuilder<DeckFamilyStatistics>();

        foreach (var definition in catalog.Families)
        {
            var versions = definition.Revisions
                .Select(revision => observed.GetValueOrDefault(revision.Key) ?? EmptyVersion(revision, definition.HeroCardId))
                .ToImmutableArray();
            assigned.UnionWith(versions.Select(static version => version.Identity.Key));
            families.Add(CreateFamily(
                definition.Id,
                definition.Name,
                definition.Format,
                versions,
                catalog.ActiveRevisionKey,
                definition.HeroCardId));
        }

        foreach (var version in observed.Values.Where(version => !assigned.Contains(version.Identity.Key)))
        {
            families.Add(CreateFamily(
                DeterministicId(version.Identity.Key),
                null,
                version.Identity.Format,
                [version],
                catalog.ActiveRevisionKey,
                version.HeroCardId));
        }

        var ordered = families
            .OrderByDescending(static family => family.IsActive)
            .ThenByDescending(static family => family.LastPlayedAt)
            .ThenBy(static family => family.Id)
            .ToImmutableArray();
        return new DeckLibrarySnapshot(ordered, catalog.ActiveRevisionKey, message, isError);
    }

    public static Guid DeterministicId(string revisionKey)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(revisionKey));
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static DeckVersionStatistics? CreateVersion(
        HistoryDeck deck,
        Dictionary<string, string> latestHeroes)
    {
        var identity = DeckRevisionIdentity.FromHistory(deck);
        if (identity is null)
        {
            return null;
        }

        latestHeroes.TryGetValue(identity.Key, out var heroCardId);
        return new DeckVersionStatistics(
            identity,
            deck.Games,
            deck.Wins,
            deck.Losses,
            deck.Ties,
            deck.UnknownResults,
            deck.LastPlayedAt,
            deck.Confidence,
            heroCardId);
    }

    private static DeckFamilyStatistics CreateFamily(
        Guid id,
        string? name,
        string format,
        ImmutableArray<DeckVersionStatistics> versions,
        string? activeRevisionKey,
        string? fallbackHeroCardId)
    {
        var active = activeRevisionKey is not null &&
                     versions.Any(version => string.Equals(
                         version.Identity.Key,
                         activeRevisionKey,
                         StringComparison.Ordinal));
        var current = active
            ? versions.First(version => string.Equals(version.Identity.Key, activeRevisionKey, StringComparison.Ordinal))
            : versions.OrderByDescending(static version => version.LastPlayedAt).First();
        var confidence = versions
            .Select(static version => version.Confidence)
            .Aggregate(Certainty.Exact, CertaintyRules.Lowest);
        return new DeckFamilyStatistics(
            id,
            name,
            format,
            versions,
            current.Identity.Key,
            active,
            versions.Sum(static version => version.Games),
            versions.Sum(static version => version.Wins),
            versions.Sum(static version => version.Losses),
            versions.Sum(static version => version.Ties),
            versions.Sum(static version => version.UnknownResults),
            versions.Max(static version => version.LastPlayedAt),
            confidence,
            current.HeroCardId ?? fallbackHeroCardId);
    }

    private static DeckVersionStatistics EmptyVersion(DeckRevisionIdentity identity, string? heroCardId) =>
        new(identity, 0, 0, 0, 0, 0, null, Certainty.Inferred, heroCardId);

    private static Dictionary<string, string> LatestHeroesByRevision(ImmutableArray<HistoryMatch> matches)
    {
        var heroes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var match in matches.OrderBy(static match => match.EndedAt))
        {
            var identity = DeckRevisionIdentity.FromHistory(match);
            if (identity is not null && match.PlayerHeroCardId is { Length: > 0 } heroCardId)
            {
                heroes[identity.Key] = heroCardId;
            }
        }

        return heroes;
    }
}
