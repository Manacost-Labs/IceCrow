using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProfileHistoryProjectionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MixedModesAreNormalizedSortedAndCountedWithoutInventingBattlegroundsResult()
    {
        var standard = Constructed("standard", MatchResult.Won, Timestamp.AddMinutes(1));
        var wild = Constructed("wild", MatchResult.Lost, Timestamp.AddMinutes(2));
        var arena = Arena(Timestamp.AddMinutes(3));
        var battlegrounds = Battlegrounds(Timestamp.AddMinutes(4), placement: 2);

        var snapshot = ProfileHistoryProjection.Create([standard, battlegrounds, wild, arena]);

        Assert.Equal(
            [HistoryGameMode.Battlegrounds, HistoryGameMode.Arena, HistoryGameMode.Wild, HistoryGameMode.Standard],
            snapshot.Matches.Select(static match => match.Mode));
        Assert.Equal(4, snapshot.SourceEvents);
        Assert.Equal(0, snapshot.SkippedEvents);
        Assert.Equal(1, snapshot.Wins);
        Assert.Equal(1, snapshot.Losses);
        var bg = snapshot.Matches[0];
        Assert.Equal(2, bg.Placement);
        Assert.Equal(Certainty.Exact, bg.PlacementConfidence);
        Assert.Equal(MatchResult.Unknown, bg.Result);
        Assert.Equal(Certainty.Unknown, bg.ResultConfidence);
    }

    [Fact]
    public void ExactDeckIdentityAggregatesRecordsAndKeepsTheLowestCertainty()
    {
        var exact = Constructed(
            "standard",
            MatchResult.Won,
            Timestamp.AddMinutes(1),
            new DeckEvidence("DECK_CODE", "abcdef0123456789", Certainty.Exact));
        var partial = Constructed(
            "standard",
            MatchResult.Lost,
            Timestamp.AddMinutes(2),
            new DeckEvidence("DECK_CODE", "abcdef0123456789", Certainty.Partial));

        var deck = Assert.Single(ProfileHistoryProjection.Create([exact, partial]).Decks);

        Assert.Equal(HistoryGameMode.Standard, deck.Mode);
        Assert.Equal("DECK_CODE", deck.DeckCode);
        Assert.Equal("abcdef0123456789", deck.DeckHash);
        Assert.Equal(Certainty.Partial, deck.Confidence);
        Assert.Equal(2, deck.Games);
        Assert.Equal(1, deck.Wins);
        Assert.Equal(1, deck.Losses);
        Assert.Equal(0, deck.UnknownResults);
        Assert.Equal(Timestamp.AddMinutes(2), deck.LastPlayedAt);
    }

    [Fact]
    public void UnknownResultIsExcludedFromWinRateInputsAndEmptyBoundaryRecordIsHidden()
    {
        var deck = new DeckEvidence("DECK_CODE", null, Certainty.Inferred);
        var won = Constructed("standard", MatchResult.Won, Timestamp.AddMinutes(3), deck);
        var unknown = Constructed("standard", MatchResult.Unknown, Timestamp.AddMinutes(4), deck);
        var emptyBoundary = Constructed("standard", MatchResult.Unknown, Timestamp, deck);
        emptyBoundary = emptyBoundary with
        {
            Payload = ProfileEvent.Create(
                ProfileEventType.ConstructedMatch,
                Timestamp,
                new ConstructedMatchRecord(
                    Guid.CreateVersion7(), "ranked", "standard", MatchResult.Unknown, Certainty.Unknown,
                    Timestamp, Timestamp, 0, 1, null, null, deck, MulliganRecord.Unknown,
                    null, OpponentDeckEvidence.Unknown, null, 224857, 2)).Payload,
        };

        var snapshot = ProfileHistoryProjection.Create([won, unknown, emptyBoundary]);
        var aggregate = Assert.Single(snapshot.Decks);

        Assert.Equal(2, snapshot.Matches.Length);
        Assert.Equal(1, snapshot.SkippedEvents);
        Assert.Equal(1, snapshot.MatchesWithResult);
        Assert.Equal(2, aggregate.Games);
        Assert.Equal(1, aggregate.Wins);
        Assert.Equal(0, aggregate.Losses);
        Assert.Equal(1, aggregate.UnknownResults);
    }

    [Fact]
    public void SameCanonicalDeckCodeStaysOneDeckWhenOneRecordAlsoHasAHash()
    {
        var first = Constructed(
            "standard",
            MatchResult.Won,
            Timestamp.AddMinutes(1),
            new DeckEvidence("DECK_CODE", null, Certainty.Inferred));
        var second = Constructed(
            "standard",
            MatchResult.Lost,
            Timestamp.AddMinutes(2),
            new DeckEvidence("DECK_CODE", "abcdef0123456789", Certainty.Exact));

        var deck = Assert.Single(ProfileHistoryProjection.Create([first, second]).Decks);

        Assert.Equal(2, deck.Games);
        Assert.Equal("DECK_CODE", deck.DeckCode);
    }

    [Fact]
    public void UnknownDeckAndMalformedMatchAreOmittedRatherThanFabricated()
    {
        var unknownDeck = Constructed("wild", MatchResult.Won, Timestamp.AddMinutes(1));
        var malformed = ProfileEvent.Create(
            ProfileEventType.ConstructedMatch,
            Timestamp,
            new { format = "standard", result = "won" });

        var snapshot = ProfileHistoryProjection.Create([unknownDeck, malformed]);

        Assert.Single(snapshot.Matches);
        Assert.Empty(snapshot.Decks);
        Assert.Equal(1, snapshot.SkippedEvents);
        Assert.Null(snapshot.Matches[0].DeckCode);
        Assert.Equal(Certainty.Unknown, snapshot.Matches[0].DeckConfidence);
    }

    private static ProfileEvent Constructed(
        string format,
        MatchResult result,
        DateTimeOffset endedAt,
        DeckEvidence? deck = null) =>
        ProfileEvent.Create(
            ProfileEventType.ConstructedMatch,
            endedAt,
            new ConstructedMatchRecord(
                Guid.CreateVersion7(), "ranked", format, result, Certainty.Exact,
                Timestamp, endedAt, (int)(endedAt - Timestamp).TotalSeconds, 8,
                "HERO_01", "HERO_02", deck ?? DeckEvidence.Unknown,
                MulliganRecord.Unknown, null, OpponentDeckEvidence.Unknown, null, 224857, 2));

    private static ProfileEvent Arena(DateTimeOffset endedAt) =>
        ProfileEvent.Create(
            ProfileEventType.ArenaMatch,
            endedAt,
            new ArenaMatchRecord(
                Guid.CreateVersion7(), null, null, null, Certainty.Unknown,
                MatchResult.Tied, Certainty.Exact, "HERO_03", "HERO_04",
                Timestamp, endedAt, (int)(endedAt - Timestamp).TotalSeconds, 9,
                MulliganRecord.Unknown, null, 224857));

    private static ProfileEvent Battlegrounds(DateTimeOffset endedAt, int placement) =>
        ProfileEvent.Create(
            ProfileEventType.BattlegroundsMatch,
            endedAt,
            new BattlegroundsMatchRecord(
                Guid.CreateVersion7(), BattlegroundsMode.Solo, null, null, Certainty.Unknown,
                "TB_BaconShop_HERO_41", placement, Certainty.Exact,
                Timestamp, endedAt, (int)(endedAt - Timestamp).TotalSeconds, 12, null, 224857));
}
