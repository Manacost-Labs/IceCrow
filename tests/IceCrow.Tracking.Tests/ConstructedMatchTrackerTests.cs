using IceCrow.Tracking.Constructed;
using static IceCrow.Tracking.Tests.ConstructedGameScript;

namespace IceCrow.Tracking.Tests;

public sealed class ConstructedMatchTrackerTests
{
    [Fact]
    public void RankedStandardGameProducesOneSummaryWithMetadataAndResult()
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame());

        var summary = Assert.Single(script.Completed);
        Assert.Equal(GameMode.Ranked, summary.Mode);
        Assert.Equal(ConstructedFormat.Standard, summary.Format);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal(EvidenceCertainty.Exact, summary.ResultCertainty);
        Assert.Equal(12, summary.Turns);
        Assert.Equal(1, summary.LocalPlayerId);
        Assert.Equal("HERO_01", summary.PlayerHeroCardId);
        Assert.Equal("HERO_08", summary.OpponentHeroCardId);
        Assert.Equal(224857, summary.HearthstoneBuild);
        Assert.Equal(2, summary.ScenarioId);
        Assert.Equal(Timestamp, summary.StartedAt);
        // The first terminal playstate (the opponent's LOST, three lines
        // before FINAL_GAMEOVER) is the match end, not STATE=COMPLETE.
        Assert.Equal(script.LastTimestamp.AddMilliseconds(-3), summary.EndedAt);
        Assert.False(script.Tracker.IsGameOpen);
        Assert.Equal(1, script.Tracker.GamesSeen);
        Assert.Equal(1, script.Tracker.CompletedMatches);
        Assert.Equal(0, script.Tracker.IgnoredModeGames);
    }

    [Theory]
    [InlineData("FT_STANDARD", ConstructedFormat.Standard)]
    [InlineData("FT_WILD", ConstructedFormat.Wild)]
    public void RankedFormatIsTakenFromTheMetadataBlock(string token, ConstructedFormat expected)
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame(formatType: token));

        Assert.Equal(expected, Assert.Single(script.Completed).Format);
    }

    [Fact]
    public void ArenaGameIsCollectedRegardlessOfItsFormatToken()
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame(gameType: "GT_ARENA", formatType: "FT_WILD"));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(GameMode.Arena, summary.Mode);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
    }

    [Theory]
    [InlineData("GT_CASUAL", "FT_STANDARD", false)]
    [InlineData("GT_TAVERNBRAWL", "FT_WILD", false)]
    [InlineData("GT_BATTLEGROUNDS", "FT_WILD", false)]
    [InlineData("GT_RANKED", "FT_CLASSIC", true)]
    [InlineData("GT_RANKED", "FT_TWIST", true)]
    public void UnsupportedModesProduceNoSummary(string gameType, string formatType, bool entitiesTracked)
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame(gameType, formatType));

        Assert.Empty(script.Completed);
        Assert.Equal(1, script.Tracker.IgnoredModeGames);
        Assert.Equal(0, script.Tracker.CompletedMatches);
        // A mode that is never collected frees its table on the metadata
        // line and ignores the rest of the game; an unsupported ranked
        // format is still tracked because the format line may follow later.
        Assert.Equal(entitiesTracked, script.Tracker.EntityCount > 0);
    }

    [Theory]
    [InlineData("WON", ConstructedMatchResult.Won)]
    [InlineData("LOST", ConstructedMatchResult.Lost)]
    [InlineData("TIED", ConstructedMatchResult.Tied)]
    [InlineData("CONCEDED", ConstructedMatchResult.Lost)]
    public void LocalPlayStateDecidesTheResult(string playState, ConstructedMatchResult expected)
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame(localPlayState: playState, opponentPlayState: null));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(expected, summary.Result);
        Assert.Equal(EvidenceCertainty.Exact, summary.ResultCertainty);
    }

    [Theory]
    [InlineData("LOST", ConstructedMatchResult.Won)]
    [InlineData("WON", ConstructedMatchResult.Lost)]
    [InlineData("TIED", ConstructedMatchResult.Tied)]
    public void OpponentPlayStateIsMirroredWhenTheLocalPlayerIsSilent(string playState, ConstructedMatchResult expected)
    {
        var script = new ConstructedGameScript();

        script.Feed(StandardGame(localPlayState: null, opponentPlayState: playState));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(expected, summary.Result);
        Assert.Equal(EvidenceCertainty.Exact, summary.ResultCertainty);
    }

    [Fact]
    public void OpponentConcedeIsNotMirroredUntilItsLostArrives()
    {
        var script = new ConstructedGameScript();
        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));

        script.Feed("TAG_CHANGE Entity=3 tag=PLAYSTATE value=CONCEDED");
        script.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");

        Assert.Equal(ConstructedMatchResult.Unknown, Assert.Single(script.Completed).Result);

        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));
        script.Feed("TAG_CHANGE Entity=3 tag=PLAYSTATE value=CONCEDED");
        script.Feed("TAG_CHANGE Entity=3 tag=PLAYSTATE value=LOST");
        script.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");

        Assert.Equal(ConstructedMatchResult.Won, script.Completed[1].Result);
    }

    [Fact]
    public void MulliganKeepAllRecordsEveryInitialCardAsKept()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            MulliganDone(),
            Finish()));

        var mulligan = Assert.Single(script.Completed).Mulligan;
        Assert.Equal(["A", "B", "C"], mulligan.Initial);
        Assert.Equal(["A", "B", "C"], mulligan.Kept);
        Assert.Empty(mulligan.Replaced);
        Assert.Equal(["A", "B", "C"], mulligan.After);
        Assert.Equal(EvidenceCertainty.Exact, mulligan.Certainty);
    }

    [Fact]
    public void MulliganReplaceOneSplitsKeptAndReplacedAndOrdersAfterByZonePosition()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            ReplaceLocal(11, 5, "NEW", zonePosition: 2),
            MulliganDone(),
            Finish()));

        var mulligan = Assert.Single(script.Completed).Mulligan;
        Assert.Equal(["A", "B", "C"], mulligan.Initial);
        Assert.Equal(["A", "C"], mulligan.Kept);
        Assert.Equal(["B"], mulligan.Replaced);
        Assert.Equal(["A", "NEW", "C"], mulligan.After);
        Assert.Equal(EvidenceCertainty.Exact, mulligan.Certainty);
    }

    [Fact]
    public void MulliganReplaceAllLeavesNothingKept()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            ReplaceLocal(10, 4, "N1", zonePosition: 1),
            ReplaceLocal(11, 5, "N2", zonePosition: 2),
            ReplaceLocal(12, 6, "N3", zonePosition: 3),
            MulliganDone(),
            Finish()));

        var mulligan = Assert.Single(script.Completed).Mulligan;
        Assert.Empty(mulligan.Kept);
        Assert.Equal(["A", "B", "C"], mulligan.Replaced);
        Assert.Equal(["N1", "N2", "N3"], mulligan.After);
    }

    [Fact]
    public void MulliganWithDuplicateCardIdsIsPartitionedByEntityNotByCardId()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            Heroes(),
            LocalHand("X", "X", "Y"),
            OpponentHand(3),
            MulliganInput(),
            ReplaceLocal(11, 5, "Z", zonePosition: 2),
            MulliganDone(),
            Finish()));

        var mulligan = Assert.Single(script.Completed).Mulligan;
        Assert.Equal(["X", "X", "Y"], mulligan.Initial);
        Assert.Equal(["X", "Y"], mulligan.Kept);
        Assert.Equal(["X"], mulligan.Replaced);
    }

    [Fact]
    public void FirstPlayerSeesThreeCardsAndSecondPlayerSeesFourPlusTheCoinOnlyAfterward()
    {
        var first = new ConstructedGameScript();
        first.Feed(Concat(
            Header(),
            Metadata(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(4),
            MulliganInput(),
            MulliganDone(),
            Finish()));

        var second = new ConstructedGameScript();
        second.Feed(Concat(
            Header(),
            Metadata(),
            Heroes(),
            LocalHand("A", "B", "C", "D"),
            OpponentHand(3),
            MulliganInput(),
            Coin(controller: 1, zonePosition: 5),
            MulliganDone(),
            Finish()));

        Assert.Equal(3, Assert.Single(first.Completed).Mulligan.Initial.Count);
        Assert.Equal(3, Assert.Single(first.Completed).Mulligan.After.Count);
        var mulligan = Assert.Single(second.Completed).Mulligan;
        Assert.Equal(["A", "B", "C", "D"], mulligan.Initial);
        Assert.Equal(["A", "B", "C", "D"], mulligan.Kept);
        Assert.Empty(mulligan.Replaced);
        Assert.Equal(["A", "B", "C", "D", "GAME_005"], mulligan.After);
    }

    [Fact]
    public void OpponentMulliganCountsHiddenReplacementsWithoutRecordingCardIds()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput()));
        script.Feed("TAG_CHANGE Entity=41 tag=ZONE value=DECK");
        script.Feed("TAG_CHANGE Entity=34 tag=ZONE value=HAND");
        script.Feed("TAG_CHANGE Entity=34 tag=ZONE_POSITION value=2");
        script.Feed(Concat(MulliganDone(), Finish()));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(1, summary.OpponentMulliganReplacedCount);
        Assert.Empty(summary.ObservedOpponentCards);
    }

    [Fact]
    public void OpponentMulliganIsNullWhenItsDoneTransitionWasNeverSeen()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            Finish()));

        var summary = Assert.Single(script.Completed);
        Assert.Null(summary.OpponentMulliganReplacedCount);
        Assert.Equal(EvidenceCertainty.Unknown, summary.Mulligan.Certainty);
        Assert.Empty(summary.Mulligan.Initial);
    }

    [Fact]
    public void ObservedOpponentCardsAreDistinctRevealedCardsExcludingHeroesPowersAndEnchantments()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            MulliganDone(),
            Reveal(40, "CS2_029"),
            Reveal(41, "EX1_294", zone: "SECRET"),
            Reveal(42, "CS2_106", cardType: "WEAPON"),
            [
                "FULL_ENTITY - Creating ID=80 CardID=CS2_029e",
                "tag=CONTROLLER value=2",
                "tag=CARDTYPE value=ENCHANTMENT",
                "tag=ZONE value=PLAY",
            ],
            Reveal(34, "REV_990", cardType: "LOCATION"),
            Reveal(35, "CS2_029"),
            [
                "TAG_CHANGE Entity=10 tag=ZONE value=PLAY",
            ],
            Finish()));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(["CS2_029", "EX1_294", "CS2_106", "REV_990"], summary.ObservedOpponentCards);
    }

    [Fact]
    public void ObservedOpponentCardsAreBoundedByTheConfiguredLimit()
    {
        var script = new ConstructedGameScript(
            new ConstructedMatchTracker(new ConstructedMatchLimits(maximumObservedOpponentCards: 2)));

        script.Feed(Concat(
            Header(),
            Metadata(),
            OpponentDeck(),
            Heroes(),
            LocalHand("A"),
            OpponentHand(3),
            Reveal(40, "ONE"),
            Reveal(41, "TWO"),
            Reveal(42, "THREE"),
            Finish()));

        Assert.Equal(["ONE", "TWO"], Assert.Single(script.Completed).ObservedOpponentCards);
    }

    [Fact]
    public void UnknownLocalPlayerLeavesEveryPlayerRelativeFactUnknown()
    {
        var script = new ConstructedGameScript();

        // No entity is ever created with a known card id in a hand or deck,
        // so the client's own side cannot be told apart from the opponent.
        script.Feed(Concat(
            Header(),
            Metadata(),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            HiddenCards(10, 3, controller: 1, zone: "HAND"),
            OpponentHand(3),
            MulliganInput(),
            MulliganDone(),
            Reveal(40, "CS2_029"),
            Turns(4),
            Finish()));

        var summary = Assert.Single(script.Completed);
        Assert.Null(summary.LocalPlayerId);
        Assert.Equal(ConstructedMatchResult.Unknown, summary.Result);
        Assert.Equal(EvidenceCertainty.Unknown, summary.ResultCertainty);
        Assert.Null(summary.PlayerHeroCardId);
        Assert.Null(summary.OpponentHeroCardId);
        Assert.Same(ConstructedMulligan.Unknown, summary.Mulligan);
        Assert.Null(summary.OpponentMulliganReplacedCount);
        Assert.Empty(summary.ObservedOpponentCards);
        Assert.Equal(4, summary.Turns);
    }

    [Fact]
    public void EntityTableBoundCountsAndIgnoresNewEntitiesWithoutThrowing()
    {
        var script = new ConstructedGameScript(
            new ConstructedMatchTracker(new ConstructedMatchLimits(maximumTrackedEntities: 3)));

        script.Feed(StandardGame());

        Assert.Equal(3, script.Tracker.EntityCount);
        Assert.True(script.Tracker.RejectedEntities > 0);
        var summary = Assert.Single(script.Completed);
        Assert.Null(summary.LocalPlayerId);
        Assert.Equal(ConstructedMatchResult.Unknown, summary.Result);
    }

    [Fact]
    public void ConsecutiveGamesDoNotLeakStateIntoEachOther()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata("GT_RANKED", "FT_STANDARD", "224857"),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(3),
            MulliganInput(),
            MulliganDone(),
            Reveal(40, "CS2_029"),
            Turns(12),
            Finish()));
        script.Feed(Concat(
            Header(),
            Metadata("GT_RANKED", "FT_WILD", "224900"),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            LocalHand("D", "E", "F"),
            OpponentHand(3),
            MulliganInput(),
            ReplaceLocal(10, 4, "G", zonePosition: 1),
            MulliganDone(),
            Turns(5),
            Finish("LOST", "WON")));

        Assert.Equal(2, script.Completed.Count);
        var second = script.Completed[1];
        Assert.Equal(ConstructedFormat.Wild, second.Format);
        Assert.Equal(224900, second.HearthstoneBuild);
        Assert.Equal(ConstructedMatchResult.Lost, second.Result);
        Assert.Equal(5, second.Turns);
        Assert.Empty(second.ObservedOpponentCards);
        Assert.Equal(["D"], second.Mulligan.Replaced);
        Assert.Equal(["E", "F"], second.Mulligan.Kept);
        Assert.Equal(["CS2_029"], script.Completed[0].ObservedOpponentCards);
        Assert.Equal(2, script.Tracker.CompletedMatches);
    }

    [Fact]
    public void UnknownTagsValuesAndReferencesNeverThrow()
    {
        var script = new ConstructedGameScript();
        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));

        script.Feed("TAG_CHANGE Entity=1 tag=WHATEVER value=abc");
        script.Feed("TAG_CHANGE Entity=1 tag=9999 value=-3");
        script.Feed("TAG_CHANGE Entity=2 tag=ZONE value=NEW_ZONE");
        script.Feed("TAG_CHANGE Entity=2 tag=PLAYSTATE value=WEIRD");
        script.Feed("TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=LATER");
        script.Feed("TAG_CHANGE Entity=Nobody tag=TURN value=5");
        script.Feed("TAG_CHANGE Entity=1 tag=TURN value=-1");
        script.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=SOMETHING");
        script.Feed("SHOW_ENTITY - Updating Entity=Nobody CardID=XYZ");
        script.Feed(Finish());

        var summary = Assert.Single(script.Completed);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal(1, summary.Turns);
    }

    [Fact]
    public void DuplicatedLogStreamsDoNotRecaptureTheMulliganOrCompleteTwice()
    {
        // The real client prints every power line twice: GameState first,
        // then the PowerTaskList replay. A replayed INPUT after DONE must
        // not re-read the post-mulligan hand as the initial one.
        var script = new ConstructedGameScript();
        var mulligan = Concat(MulliganInput(), ReplaceLocal(11, 5, "NEW", zonePosition: 2), MulliganDone());
        script.Feed(
            Concat(Header(), Metadata(), LocalDeck(), Heroes(), LocalHand("A", "B", "C"), OpponentHand(3), mulligan),
            GameStatePrefix);

        script.Feed(mulligan);
        script.Feed(Finish(), GameStatePrefix);
        script.Feed(Finish());

        var summary = Assert.Single(script.Completed);
        Assert.Equal(["B"], summary.Mulligan.Replaced);
        Assert.Equal(["A", "C"], summary.Mulligan.Kept);
        Assert.Equal(1, script.Tracker.CompletedMatches);
    }

    [Fact]
    public void NextGameBoundaryClosesAnOpenGameThatNeverReportedComplete()
    {
        var script = new ConstructedGameScript();
        script.Feed(StandardGame(localPlayState: "WON", opponentPlayState: null).SkipLast(2));
        var endedAt = script.LastTimestamp;
        Assert.Empty(script.Completed);

        var boundary = script.Feed("CREATE_GAME");

        var summary = Assert.IsType<ConstructedMatchSummary>(boundary.CompletedMatch);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal(endedAt, summary.EndedAt);
        Assert.True(script.Tracker.IsGameOpen);
        Assert.Equal(2, script.Tracker.GamesSeen);
    }

    [Fact]
    public void BoundaryClosedGameWithoutResultIsEmittedOnlyWhenATurnWasSeen()
    {
        var withoutTurn = new ConstructedGameScript();
        withoutTurn.Feed(Concat(Header(withTurn: false), Metadata(), Heroes(), LocalHand("A")));
        withoutTurn.Feed("CREATE_GAME");

        Assert.Empty(withoutTurn.Completed);
        Assert.Equal(0, withoutTurn.Tracker.IgnoredModeGames);

        var withTurn = new ConstructedGameScript();
        withTurn.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A")));
        var boundary = withTurn.Feed("CREATE_GAME");

        var summary = Assert.IsType<ConstructedMatchSummary>(boundary.CompletedMatch);
        Assert.Equal(ConstructedMatchResult.Unknown, summary.Result);
        Assert.Equal(EvidenceCertainty.Unknown, summary.ResultCertainty);
        Assert.Equal(withTurn.LastTimestamp, summary.EndedAt);
    }

    [Fact]
    public void BareNamePlayerReferencesResolveOnlyThroughAProvenDescriptor()
    {
        var unresolved = new ConstructedGameScript();
        unresolved.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));
        unresolved.Feed("TAG_CHANGE Entity=Player#1234 tag=PLAYSTATE value=WON");
        unresolved.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");
        Assert.Equal(ConstructedMatchResult.Unknown, Assert.Single(unresolved.Completed).Result);

        var resolved = new ConstructedGameScript();
        resolved.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));
        resolved.Feed("TAG_CHANGE Entity=[name=Player#1234 id=2 zone=PLAY zonePos=0 cardId= player=1] tag=TIMEOUT value=75");
        resolved.Feed("TAG_CHANGE Entity=GameEntity tag=TURN value=7");
        resolved.Feed("TAG_CHANGE Entity=Player#1234 tag=PLAYSTATE value=WON");
        resolved.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");

        var summary = Assert.Single(resolved.Completed);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal(7, summary.Turns);
    }

    [Fact]
    public void AmbiguousNamesArePoisonedAndNeverResolve()
    {
        var script = new ConstructedGameScript();
        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));
        script.Feed("TAG_CHANGE Entity=[name=Twin id=2 zone=PLAY zonePos=0 cardId= player=1] tag=TIMEOUT value=75");
        script.Feed("TAG_CHANGE Entity=[name=Twin id=3 zone=PLAY zonePos=0 cardId= player=2] tag=TIMEOUT value=75");

        script.Feed("TAG_CHANGE Entity=Twin tag=PLAYSTATE value=WON");
        script.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");

        Assert.Equal(ConstructedMatchResult.Unknown, Assert.Single(script.Completed).Result);
    }

    [Fact]
    public void OpponentCoinCreatedWithAKnownCardIdDoesNotOverrideTheLocalPlayer()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(
            Header(),
            Metadata(),
            Heroes(),
            LocalHand("A", "B", "C"),
            OpponentHand(4),
            Coin(controller: 2, zonePosition: 5),
            Finish()));

        var summary = Assert.Single(script.Completed);
        Assert.Equal(1, summary.LocalPlayerId);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal(["GAME_005"], summary.ObservedOpponentCards);
    }

    [Fact]
    public void CompleteBeforeAnyTerminalPlayStateEndsTheGameWithAnUnknownResult()
    {
        var script = new ConstructedGameScript();
        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));

        script.Feed("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");
        script.Feed("TAG_CHANGE Entity=2 tag=PLAYSTATE value=WON");

        var summary = Assert.Single(script.Completed);
        Assert.Equal(ConstructedMatchResult.Unknown, summary.Result);
        Assert.Equal(1, script.Tracker.CompletedMatches);
        Assert.False(script.Tracker.IsGameOpen);
    }

    [Fact]
    public void MetadataPrintedBeforeCreateGameStillClassifiesTheNextGame()
    {
        var script = new ConstructedGameScript();

        script.Feed(Concat(Metadata("GT_RANKED", "FT_STANDARD"), Header(), Heroes(), LocalHand("A"), OpponentHand(1), Finish()));
        script.Feed(Concat(Metadata("GT_RANKED", "FT_WILD"), Header(), Heroes(), LocalHand("A"), OpponentHand(1), Finish()));

        Assert.Equal(2, script.Completed.Count);
        Assert.Equal(ConstructedFormat.Standard, script.Completed[0].Format);
        Assert.Equal(ConstructedFormat.Wild, script.Completed[1].Format);
    }

    [Fact]
    public void ResetDropsTheOpenGameUntilTheNextBoundary()
    {
        var script = new ConstructedGameScript();
        script.Feed(Concat(Header(), Metadata(), Heroes(), LocalHand("A"), OpponentHand(1)));

        script.Tracker.Reset();
        script.Feed(Finish());

        Assert.Empty(script.Completed);
        Assert.False(script.Tracker.IsGameOpen);
        Assert.Equal(GameMode.Unknown, script.Tracker.Metadata.Mode);

        script.Feed(StandardGame());

        Assert.Single(script.Completed);
    }
}
