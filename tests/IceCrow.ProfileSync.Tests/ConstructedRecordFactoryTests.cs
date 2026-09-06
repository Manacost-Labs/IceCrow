using IceCrow.ProfileSync.Factories;
using IceCrow.ProfileSync.Records;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.ProfileSync.Tests;

public sealed class ConstructedRecordFactoryTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid MatchId = Guid.Parse("11111111-2222-7333-8444-555555555555");

    [Fact]
    public void RankedStandardSummaryMapsToAConstructedRecord()
    {
        var record = ConstructedRecordFactory.CreateRanked(Summary(), MatchId);

        Assert.NotNull(record);
        Assert.Equal(MatchId, record.MatchId);
        Assert.Equal("ranked", record.GameType);
        Assert.Equal("standard", record.Format);
        Assert.Equal(MatchResult.Won, record.Result);
        Assert.Equal(Certainty.Exact, record.ResultConfidence);
        Assert.Equal(Timestamp, record.StartedAt);
        Assert.Equal(Timestamp.AddMinutes(9), record.EndedAt);
        Assert.Equal(540, record.DurationSeconds);
        Assert.Equal(14, record.Turns);
        Assert.Equal("HERO_01", record.PlayerHeroCardId);
        Assert.Equal("HERO_08", record.OpponentHeroCardId);
        Assert.Same(DeckEvidence.Unknown, record.PlayerDeck);
        Assert.Equal(["A", "B", "C"], record.PlayerMulligan.Initial);
        Assert.Equal(["A", "C"], record.PlayerMulligan.Kept);
        Assert.Equal(["B"], record.PlayerMulligan.Replaced);
        Assert.Equal(["A", "C", "D"], record.PlayerMulligan.After);
        Assert.Equal(Certainty.Exact, record.PlayerMulligan.Confidence);
        Assert.Equal(1, record.OpponentMulliganReplacedCount);
        Assert.Equal(["CS2_029", "EX1_294"], record.OpponentDeck.ObservedCards);
        Assert.Null(record.OpponentDeck.DeckCode);
        Assert.Null(record.OpponentDeck.DeckHash);
        Assert.Equal(Certainty.Partial, record.OpponentDeck.Confidence);
        Assert.Null(record.GameJoinEvidence);
        Assert.Equal(224857, record.HearthstoneBuild);
        Assert.Equal(2, record.ScenarioId);
    }

    [Fact]
    public void RankedWildSummaryUsesTheWildFormat()
    {
        var record = ConstructedRecordFactory.CreateRanked(Summary(format: ConstructedFormat.Wild), MatchId);

        Assert.Equal("wild", record?.Format);
    }

    [Fact]
    public void ArenaSummaryMapsToAnArenaRecordWithoutInventedScores()
    {
        var record = ConstructedRecordFactory.CreateArena(Summary(mode: GameMode.Arena), MatchId);

        Assert.NotNull(record);
        Assert.Equal(MatchId, record.MatchId);
        Assert.Null(record.RunId);
        Assert.Null(record.ScoreBefore);
        Assert.Null(record.ScoreAfter);
        Assert.Equal(Certainty.Unknown, record.ScoreConfidence);
        Assert.Equal(MatchResult.Won, record.Result);
        Assert.Equal(Certainty.Exact, record.ResultConfidence);
        Assert.Equal("HERO_01", record.PlayerHeroCardId);
        Assert.Equal(540, record.DurationSeconds);
        Assert.Equal(14, record.Turns);
        Assert.Equal(["B"], record.PlayerMulligan.Replaced);
        Assert.Equal(1, record.OpponentMulliganReplacedCount);
        Assert.Equal(224857, record.HearthstoneBuild);
    }

    [Theory]
    [InlineData(EvidenceCertainty.Unknown, Certainty.Unknown)]
    [InlineData(EvidenceCertainty.Inferred, Certainty.Inferred)]
    [InlineData(EvidenceCertainty.Partial, Certainty.Partial)]
    [InlineData(EvidenceCertainty.Exact, Certainty.Exact)]
    [InlineData((EvidenceCertainty)99, Certainty.Unknown)]
    public void CertaintyMapsOneWayAndIsNeverRaised(EvidenceCertainty source, Certainty expected)
    {
        var mapped = EvidenceCertaintyMapping.ToCertainty(source);

        Assert.Equal(expected, mapped);
        Assert.True((int)mapped <= (int)source);
    }

    [Fact]
    public void RecordCertaintiesNeverExceedTheSummaryCertainties()
    {
        var summary = Summary(
            resultCertainty: EvidenceCertainty.Inferred,
            mulligan: new ConstructedMulligan(["A"], ["A"], [], ["A"], EvidenceCertainty.Partial),
            observedOpponentCards: []);

        var record = ConstructedRecordFactory.CreateRanked(summary, MatchId);

        Assert.NotNull(record);
        Assert.Equal(Certainty.Inferred, record.ResultConfidence);
        Assert.Equal(Certainty.Partial, record.PlayerMulligan.Confidence);
        Assert.Equal(Certainty.Unknown, record.OpponentDeck.Confidence);
        Assert.Equal(Certainty.Unknown, record.PlayerDeck.Confidence);
    }

    [Fact]
    public void UnknownMulliganMapsToTheSharedUnknownRecord()
    {
        var record = ConstructedRecordFactory.CreateRanked(Summary(mulligan: ConstructedMulligan.Unknown), MatchId);

        Assert.Same(MulliganRecord.Unknown, record?.PlayerMulligan);
    }

    [Fact]
    public void ListsNumbersAndCardIdsAreClampedToTheProfileLimits()
    {
        var manyCards = Enumerable.Range(0, 70).Select(static index => $"CARD_{index}").ToArray();
        var longHand = Enumerable.Range(0, 12).Select(static index => $"HAND_{index}").ToArray();
        var overlongId = new string('X', ProfileRecordLimits.MaximumCardIdLength + 1);
        var summary = Summary(
            endedAt: Timestamp.AddHours(10),
            turns: 500,
            playerHeroCardId: overlongId,
            mulligan: new ConstructedMulligan(longHand, longHand, [], [overlongId, "OK"], EvidenceCertainty.Exact),
            observedOpponentCards: [.. manyCards, overlongId],
            opponentMulliganReplacedCount: -1);

        var record = ConstructedRecordFactory.CreateRanked(summary, MatchId);

        Assert.NotNull(record);
        Assert.Equal(ProfileRecordLimits.MaximumDurationSeconds, record.DurationSeconds);
        Assert.Equal(ProfileRecordLimits.MaximumTurns, record.Turns);
        Assert.Null(record.PlayerHeroCardId);
        Assert.Equal(ProfileRecordLimits.MaximumMulliganCards, record.PlayerMulligan.Initial.Count);
        Assert.Equal(["OK"], record.PlayerMulligan.After);
        Assert.Equal(ProfileRecordLimits.MaximumObservedOpponentCards, record.OpponentDeck.ObservedCards.Count);
        Assert.Null(record.OpponentMulliganReplacedCount);
    }

    [Fact]
    public void EndBeforeStartClampsTheDurationToZero()
    {
        var record = ConstructedRecordFactory.CreateRanked(Summary(endedAt: Timestamp.AddMinutes(-1)), MatchId);

        Assert.Equal(0, record?.DurationSeconds);
    }

    [Theory]
    [InlineData(GameMode.Casual, ConstructedFormat.Standard)]
    [InlineData(GameMode.Other, ConstructedFormat.Wild)]
    [InlineData(GameMode.Battlegrounds, ConstructedFormat.Unknown)]
    [InlineData(GameMode.Unknown, ConstructedFormat.Standard)]
    [InlineData(GameMode.Ranked, ConstructedFormat.Classic)]
    [InlineData(GameMode.Ranked, ConstructedFormat.Twist)]
    [InlineData(GameMode.Ranked, ConstructedFormat.Unknown)]
    [InlineData(GameMode.Arena, ConstructedFormat.Wild)]
    public void UnsupportedRankedSummariesReturnNull(GameMode mode, ConstructedFormat format)
    {
        Assert.Null(ConstructedRecordFactory.CreateRanked(Summary(mode, format), MatchId));
    }

    [Theory]
    [InlineData(GameMode.Ranked)]
    [InlineData(GameMode.Casual)]
    [InlineData(GameMode.Battlegrounds)]
    [InlineData(GameMode.Unknown)]
    public void NonArenaSummariesReturnNullForArena(GameMode mode)
    {
        Assert.Null(ConstructedRecordFactory.CreateArena(Summary(mode), MatchId));
    }

    [Fact]
    public void EmptyMatchIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ConstructedRecordFactory.CreateRanked(Summary(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => ConstructedRecordFactory.CreateArena(Summary(mode: GameMode.Arena), Guid.Empty));
    }

    private static ConstructedMatchSummary Summary(
        GameMode mode = GameMode.Ranked,
        ConstructedFormat format = ConstructedFormat.Standard,
        EvidenceCertainty resultCertainty = EvidenceCertainty.Exact,
        DateTimeOffset? endedAt = null,
        int turns = 14,
        string? playerHeroCardId = "HERO_01",
        ConstructedMulligan? mulligan = null,
        IReadOnlyList<string>? observedOpponentCards = null,
        int? opponentMulliganReplacedCount = 1) => new(
            mode,
            format,
            ConstructedMatchResult.Won,
            resultCertainty,
            Timestamp,
            endedAt ?? Timestamp.AddMinutes(9),
            turns,
            LocalPlayerId: 1,
            playerHeroCardId,
            "HERO_08",
            mulligan ?? new ConstructedMulligan(["A", "B", "C"], ["A", "C"], ["B"], ["A", "C", "D"], EvidenceCertainty.Exact),
            opponentMulliganReplacedCount,
            observedOpponentCards ?? ["CS2_029", "EX1_294"],
            HearthstoneBuild: 224857,
            ScenarioId: 2);
}
