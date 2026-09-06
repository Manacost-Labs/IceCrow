using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Arena;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ArenaRunCollectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ThirtyCards = Enumerable.Range(0, 30).Select(static index => $"CARD_{index:D2}").ToArray();

    [Fact]
    public void DraftStartOpensARunWithoutEmittingEvents()
    {
        var collector = new ArenaRunCollector();

        var events = collector.Observe(Drafting(0, ["A", "B", "C"]));

        Assert.Empty(events);
        Assert.NotNull(collector.Status.CurrentRunId);
        Assert.True(collector.Status.IsDrafting);
        Assert.Equal(1, collector.Status.RunsStarted);
        Assert.Equal(ClientStateProviderStatus.Connected, collector.Status.Availability);
    }

    [Fact]
    public void PickRecordsOfferedOptionsInOrderAndTheChosenCard()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, ["X", "Y", "Z"]));

        var events = collector.Observe(Drafting(1, ["P", "Q", "R"], ["Y"]));

        var single = Assert.Single(events);
        Assert.Equal(ProfileEventType.ArenaDraftPick, single.Type);
        var pick = Payload<ArenaDraftPickRecord>(single);
        Assert.Equal(collector.Status.CurrentRunId, pick.RunId);
        Assert.Equal(0, pick.PickIndex);
        Assert.Equal(["X", "Y", "Z"], pick.OfferedCardIds);
        Assert.Equal("Y", pick.ChosenCardId);
        Assert.Equal(Certainty.Exact, pick.Confidence);
        Assert.Equal(Start.AddSeconds(1), pick.ObservedAt);
        Assert.Equal(ArenaEventIds.ForDraftPick(pick.RunId, 0), single.EventId);
        Assert.Equal(1, collector.Status.PickCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void OffersAreNotAssumedToBeThree(int offerSize)
    {
        var offer = Enumerable.Range(0, offerSize).Select(static index => $"OFFER_{index}").ToArray();
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, offer));

        var events = collector.Observe(Drafting(1, ["N1", "N2"], [offer[^1]]));

        var pick = Payload<ArenaDraftPickRecord>(Assert.Single(events));
        Assert.Equal(offer, pick.OfferedCardIds);
        Assert.Equal(offer[^1], pick.ChosenCardId);
    }

    [Fact]
    public void AmbiguousTransitionsEmitNothingAndAreCountedAsGaps()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, ["A", "B", "C"]));
        Assert.Single(collector.Observe(Drafting(1, ["D", "E", "F"], ["A"])));

        Assert.Empty(collector.Observe(Drafting(2, ["G", "H", "I"], ["A", "D", "G"])));
        Assert.Equal(1, collector.Status.GapCount);
        Assert.Empty(collector.Observe(Drafting(3, ["J", "K", "L"], ["A", "D", "G", "Z"])));
        Assert.Equal(2, collector.Status.GapCount);

        var pick = Payload<ArenaDraftPickRecord>(Assert.Single(collector.Observe(Drafting(4, ["M", "N", "O"], ["A", "D", "G", "Z", "J"]))));
        Assert.Equal(["J", "K", "L"], pick.OfferedCardIds);
        Assert.Equal("J", pick.ChosenCardId);
        Assert.Equal(1, pick.PickIndex);
    }

    [Fact]
    public void HeroOfferAndLaggingDeckReadsDoNotMisalignPicks()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, ["HERO_A", "HERO_B", "HERO_C"], hero: null));
        Assert.Empty(collector.Observe(Drafting(1, ["A", "B", "C"], hero: "HERO_B")));
        Assert.Empty(collector.Observe(Drafting(2, ["D", "E", "F"], hero: "HERO_B")));

        var first = Payload<ArenaDraftPickRecord>(Assert.Single(collector.Observe(Drafting(3, ["D", "E", "F"], ["B"], hero: "HERO_B"))));
        var second = Payload<ArenaDraftPickRecord>(Assert.Single(collector.Observe(Drafting(4, ["G", "H", "I"], ["B", "E"], hero: "HERO_B"))));

        Assert.Equal(["A", "B", "C"], first.OfferedCardIds);
        Assert.Equal(("B", 0), (first.ChosenCardId, first.PickIndex));
        Assert.Equal(["D", "E", "F"], second.OfferedCardIds);
        Assert.Equal(("E", 1), (second.ChosenCardId, second.PickIndex));
        Assert.Equal(0, collector.Status.GapCount);
    }

    [Fact]
    public void FinalDeckIsExactWithACodeOrThirtyCardsAndPartialOtherwise()
    {
        var thirty = RunAfter(Playing(1, 0, 0));
        Assert.Equal(Certainty.Exact, thirty.FinalDeck.Confidence);
        Assert.Null(thirty.FinalDeck.DeckCode);
        Assert.Equal(DeckHash.Compute(ThirtyCards), thirty.FinalDeck.DeckHash);
        Assert.Equal(ThirtyCards, thirty.FinalDeckCardIds);
        Assert.Equal("HERO_04", thirty.HeroCardId);
        Assert.Equal(Certainty.Exact, thirty.ScoreConfidence);
        Assert.Equal(Start, thirty.StartedAt);
        Assert.Null(thirty.EndedAt);
        Assert.False(thirty.IsComplete);

        var partial = RunAfter(Playing(1, 0, 0, deck: ThirtyCards.Take(29)));
        Assert.Equal(Certainty.Partial, partial.FinalDeck.Confidence);

        var coded = RunAfter(Playing(1, 0, 0, deck: ThirtyCards.Take(5), deckCode: "AAECAf0EAA=="));
        Assert.Equal(Certainty.Exact, coded.FinalDeck.Confidence);
        Assert.Equal("AAECAf0EAA==", coded.FinalDeck.DeckCode);

        Assert.Equal(DeckHash.Compute(["B", "A"]), DeckHash.Compute(["A", "B"]));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("A\nB\n"))), DeckHash.Compute(["B", "A"]));
    }

    [Fact]
    public void ScoreChangesReEmitTheRunWithIdempotentIds()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0));
        var first = Assert.Single(collector.Observe(Playing(1, 0, 0)));
        var runId = collector.Status.CurrentRunId!.Value;

        Assert.Equal(ProfileEventType.ArenaRun, first.Type);
        Assert.Equal(ArenaEventIds.ForRunRecord(runId, 0, 0, false), first.EventId);
        Assert.Empty(collector.Observe(Playing(2, 0, 0)));

        var second = Assert.Single(collector.Observe(Playing(3, 1, 0)));
        Assert.Equal(ArenaEventIds.ForRunRecord(runId, 1, 0, false), second.EventId);
        Assert.NotEqual(first.EventId, second.EventId);
        Assert.Equal((1, 0), (Payload<ArenaRunRecord>(second).Wins, Payload<ArenaRunRecord>(second).Losses));
        Assert.Single(collector.Observe(Playing(4, 1, 1)));
        Assert.Equal(3, collector.Status.EmittedEvents);
    }

    [Fact]
    public void ArenaEventIdsAreDeterministicVersion8Uuids()
    {
        var runId = Guid.CreateVersion7();

        var id = ArenaEventIds.ForRunRecord(runId, 1, 0, false);

        Assert.Equal(id, ArenaEventIds.ForRunRecord(runId, 1, 0, false));
        Assert.NotEqual(id, ArenaEventIds.ForRunRecord(runId, 1, 0, true));
        Assert.NotEqual(id, ArenaEventIds.ForRunRecord(runId, 0, 1, false));
        Assert.NotEqual(id, ArenaEventIds.ForRunRecord(Guid.CreateVersion7(), 1, 0, false));
        Assert.NotEqual(ArenaEventIds.ForDraftPick(runId, 1), ArenaEventIds.ForDraftPick(runId, 2));
        var canonical = id.ToString("D");
        Assert.Equal('8', canonical[14]);
        Assert.Contains(canonical[19], "89ab");
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void RunCompletionComesOnlyFromTheClientFlag()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0));

        var twelveWins = Payload<ArenaRunRecord>(Assert.Single(collector.Observe(Playing(1, 12, 2))));
        Assert.False(twelveWins.IsComplete);
        Assert.Null(twelveWins.EndedAt);

        var threeLosses = Payload<ArenaRunRecord>(Assert.Single(collector.Observe(Playing(2, 12, 3, complete: false))));
        Assert.False(threeLosses.IsComplete);

        var complete = Payload<ArenaRunRecord>(Assert.Single(collector.Observe(Playing(3, 12, 3, complete: true))));
        Assert.True(complete.IsComplete);
        Assert.Equal(Start.AddSeconds(3), complete.EndedAt);
        Assert.Empty(collector.Observe(Playing(4, 12, 3, complete: true)));
    }

    [Fact]
    public void RatingIsExactOnlyWhenObservedBeforeAndAfterTheRun()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, rating: 5000));

        var open = Payload<ArenaRunRecord>(Assert.Single(collector.Observe(Playing(1, 0, 0, rating: 5000))));
        Assert.Equal(5000, open.RatingBefore);
        Assert.Null(open.RatingAfter);
        Assert.Equal(Certainty.Unknown, open.RatingConfidence);

        var finished = Payload<ArenaRunRecord>(Assert.Single(collector.Observe(Playing(2, 0, 3, complete: true, rating: 4950))));
        Assert.Equal(5000, finished.RatingBefore);
        Assert.Equal(4950, finished.RatingAfter);
        Assert.Equal(Certainty.Exact, finished.RatingConfidence);

        var unrated = RunAfter(Playing(1, 0, 3, complete: true));
        Assert.Null(unrated.RatingBefore);
        Assert.Null(unrated.RatingAfter);
        Assert.Equal(Certainty.Unknown, unrated.RatingConfidence);
    }

    [Fact]
    public void MatchAssociationIsExactPartialOrUnknown()
    {
        var collector = new ArenaRunCollector();
        Assert.Equal(ArenaMatchAssociation.Unknown, collector.Associate(Start, Start.AddSeconds(1)));
        collector.Observe(Drafting(0));
        collector.Observe(Playing(10, 0, 0));
        collector.Observe(Playing(31, 1, 0));
        var runId = collector.Status.CurrentRunId;

        Assert.Equal(new ArenaMatchAssociation(runId, 0, 1, Certainty.Exact), collector.Associate(Start.AddSeconds(20), Start.AddSeconds(30)));

        collector.Observe(Playing(71, 1, 2));
        Assert.Equal(new ArenaMatchAssociation(runId, null, null, Certainty.Unknown), collector.Associate(Start.AddSeconds(40), Start.AddSeconds(50)));
        Assert.Equal(new ArenaMatchAssociation(runId, null, null, Certainty.Unknown), collector.Associate(Start.AddSeconds(60), Start.AddSeconds(70)));
        Assert.Equal(new ArenaMatchAssociation(runId, 1, null, Certainty.Partial), collector.Associate(Start.AddSeconds(80), Start.AddSeconds(90)));
        Assert.Equal(ArenaMatchAssociation.Unknown, collector.Associate(Start.AddSeconds(-10), Start.AddSeconds(-5)));

        collector.Observe(Playing(100, 1, 3, complete: true));
        Assert.Equal(new ArenaMatchAssociation(runId, 1, 1, Certainty.Exact), collector.Associate(Start.AddSeconds(80), Start.AddSeconds(99)));
        Assert.Equal(ArenaMatchAssociation.Unknown, collector.Associate(Start.AddSeconds(110), Start.AddSeconds(120)));
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Associate(Start.AddSeconds(5), Start));
    }

    [Fact]
    public void NewRunKeyOrAShrinkingDeckStartsANewRun()
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, runKey: "run-1"));
        var first = collector.Status.CurrentRunId;

        collector.Observe(Drafting(1, runKey: "run-2"));
        var second = collector.Status.CurrentRunId;
        Assert.NotEqual(first, second);
        Assert.Equal(2, collector.Status.RunsStarted);

        collector.Observe(Drafting(2, deck: ["A", "B"], runKey: null));
        Assert.Equal(second, collector.Status.CurrentRunId);

        collector.Observe(Drafting(3, deck: ["A"], runKey: null));
        Assert.NotEqual(second, collector.Status.CurrentRunId);
        Assert.Equal(3, collector.Status.RunsStarted);

        collector.Observe(Playing(4, 0, 0, runKey: null));
        Assert.Equal(3, collector.Status.RunsStarted);
        collector.Observe(Drafting(5, runKey: null));
        Assert.Equal(4, collector.Status.RunsStarted);
        Assert.False(collector.Observe(Playing(6, 0, 0, runKey: null)).Count == 0);
    }

    [Fact]
    public void UnavailableSnapshotsEmitNothingAndKeepTheOpenRun()
    {
        var fresh = new ArenaRunCollector();
        Assert.Empty(fresh.Observe(null));
        Assert.Equal(ClientStateProviderStatus.Unavailable, fresh.Status.Availability);
        Assert.Null(fresh.Status.CurrentRunId);

        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0, ["A", "B", "C"]));
        var runId = collector.Status.CurrentRunId;

        Assert.Empty(collector.Observe(null));
        Assert.Equal(ClientStateProviderStatus.Unavailable, collector.Status.Availability);
        Assert.Equal(runId, collector.Status.CurrentRunId);

        var pick = Payload<ArenaDraftPickRecord>(Assert.Single(collector.Observe(Drafting(2, ["D", "E", "F"], ["A"]))));
        Assert.Equal(runId, pick.RunId);
        Assert.Equal(ClientStateProviderStatus.Connected, collector.Status.Availability);
        Assert.Equal(2, collector.Status.ObservedSnapshots);
    }

    private static ArenaRunRecord RunAfter(ArenaClientSnapshot playing)
    {
        var collector = new ArenaRunCollector();
        collector.Observe(Drafting(0));
        return Payload<ArenaRunRecord>(Assert.Single(collector.Observe(playing)));
    }

    private static T Payload<T>(ProfileEvent profileEvent) =>
        JsonSerializer.Deserialize<T>(profileEvent.Payload, ProfileJson.Options)!;

    private static ArenaClientSnapshot Drafting(
        int seconds,
        IEnumerable<string>? choices = null,
        IEnumerable<string>? deck = null,
        string? runKey = "run-1",
        string? hero = "HERO_04",
        int? rating = null) =>
        new(Start.AddSeconds(seconds), runKey, true, 0, 0, null, hero, choices ?? [], deck ?? [], null, rating);

    private static ArenaClientSnapshot Playing(
        int seconds,
        int wins,
        int losses,
        bool? complete = null,
        IEnumerable<string>? deck = null,
        string? deckCode = null,
        int? rating = null,
        string? runKey = "run-1") =>
        new(Start.AddSeconds(seconds), runKey, false, wins, losses, complete, "HERO_04", [], deck ?? ThirtyCards, deckCode, rating);
}
