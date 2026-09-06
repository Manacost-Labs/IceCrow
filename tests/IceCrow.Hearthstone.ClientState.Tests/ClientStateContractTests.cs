namespace IceCrow.Hearthstone.ClientState.Tests;

public sealed class ClientStateContractTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SelectedDeckCopiesCardsAndIsValueEqual()
    {
        var source = new[] { "CS2_029", "CS2_030" };
        var first = new SelectedDeckSnapshot(ObservedAt, "AAECAf0E", "HERO_08", "standard", source);
        source[0] = "MUTATED";
        var second = new SelectedDeckSnapshot(ObservedAt, "AAECAf0E", "HERO_08", "standard", ["CS2_029", "CS2_030"]);

        Assert.Equal("CS2_029", first.CardIds[0]);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new SelectedDeckSnapshot(ObservedAt, null, null, null, []));
    }

    [Fact]
    public void SelectedDeckRejectsOversizedInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            new SelectedDeckSnapshot(ObservedAt, new string('A', SelectedDeckSnapshot.MaximumDeckCodeLength + 1), null, null, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SelectedDeckSnapshot(ObservedAt, null, null, null, Enumerable.Range(0, SelectedDeckSnapshot.MaximumCards + 1).Select(index => $"CARD_{index}")));
        Assert.Throws<ArgumentException>(() =>
            new SelectedDeckSnapshot(ObservedAt, null, null, null, [new string('B', 65)]));
        Assert.Throws<ArgumentException>(() =>
            new SelectedDeckSnapshot(ObservedAt, null, null, null, [" "]));
        Assert.Throws<ArgumentNullException>(() =>
            new SelectedDeckSnapshot(ObservedAt, null, null, null, null!));
    }

    [Fact]
    public void CollectionCardValidatesCountsAndKeepsOptionalFinishesNull()
    {
        var card = new CollectionCard("CS2_029", 2, 1, null, 0);

        Assert.Null(card.SignatureCount);
        Assert.Equal(0, card.DiamondCount);
        Assert.Equal(card, new CollectionCard("CS2_029", 2, 1, null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionCard("CS2_029", -1, 0, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionCard("CS2_029", 0, CollectionCard.MaximumCount + 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionCard("CS2_029", 0, 0, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionCard("CS2_029", 0, 0, null, 100));
        Assert.Throws<ArgumentException>(() => new CollectionCard("", 0, 0, null, null));
        Assert.Throws<ArgumentException>(() => new CollectionCard(new string('C', 65), 0, 0, null, null));
    }

    [Fact]
    public void CollectionSnapshotIsBoundedAndValueEqual()
    {
        var cards = new[] { new CollectionCard("A", 1, 0, null, null), new CollectionCard("B", 0, 2, 1, null) };
        var first = new CollectionSnapshot(ObservedAt, cards);
        var second = new CollectionSnapshot(ObservedAt, cards.ToList());

        Assert.Equal(first, second);
        Assert.Equal(2, first.Cards.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionSnapshot(
            ObservedAt,
            Enumerable.Range(0, CollectionSnapshot.MaximumCards + 1).Select(index => new CollectionCard($"C{index}", 1, 0, null, null))));
        Assert.Throws<ArgumentException>(() => new CollectionSnapshot(ObservedAt, [cards[0], null!]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(ArenaClientSnapshot.MaximumChoices)]
    public void ArenaChoicesAreNotAssumedToBeExactlyThree(int choiceCount)
    {
        var choices = Enumerable.Range(0, choiceCount).Select(index => $"CHOICE_{index}").ToArray();

        var snapshot = new ArenaClientSnapshot(ObservedAt, null, true, 0, 0, null, null, choices, [], null, null);

        Assert.Equal(choices, snapshot.CurrentChoiceCardIds);
    }

    [Fact]
    public void ArenaSnapshotRejectsOutOfContractValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena(choices: Enumerable.Range(0, ArenaClientSnapshot.MaximumChoices + 1).Select(index => $"C{index}")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena(deck: Enumerable.Range(0, ArenaClientSnapshot.MaximumDeckCards + 1).Select(index => $"D{index}")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena(wins: ArenaClientSnapshot.MaximumScore + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena(losses: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena(rating: -1));
        Assert.Throws<ArgumentException>(() => Arena(runKey: new string('K', ArenaClientSnapshot.MaximumRunKeyLength + 1)));
        Assert.Throws<ArgumentException>(() => Arena(deckCode: " "));
    }

    [Fact]
    public void ArenaSnapshotKeepsNullsAndIsValueEqual()
    {
        var first = Arena(runKey: "run-1", isRunComplete: null, rating: null);
        var second = Arena(runKey: "run-1", isRunComplete: null, rating: null);

        Assert.Null(first.IsRunComplete);
        Assert.Null(first.Rating);
        Assert.Null(first.DeckCode);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Arena(runKey: "run-2"));
        Assert.NotEqual(first, Arena(runKey: "run-1", choices: ["B", "A"]));
    }

    [Fact]
    public void BattlegroundsRatingAllowsMissingModesAndRejectsNegativeValues()
    {
        var snapshot = new BattlegroundsRatingSnapshot(ObservedAt, 6543, null);

        Assert.Equal(6543, snapshot.SoloRating);
        Assert.Null(snapshot.DuosRating);
        Assert.Equal(snapshot, new BattlegroundsRatingSnapshot(ObservedAt, 6543, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattlegroundsRatingSnapshot(ObservedAt, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattlegroundsRatingSnapshot(ObservedAt, null, BattlegroundsRatingSnapshot.MaximumRating + 1));
    }

    private static ArenaClientSnapshot Arena(
        string? runKey = null,
        bool isDrafting = true,
        int wins = 0,
        int losses = 0,
        bool? isRunComplete = null,
        IEnumerable<string>? choices = null,
        IEnumerable<string>? deck = null,
        string? deckCode = null,
        int? rating = null) =>
        new(
            ObservedAt,
            runKey,
            isDrafting,
            wins,
            losses,
            isRunComplete,
            "HERO_01",
            choices ?? ["A", "B"],
            deck ?? [],
            deckCode,
            rating);
}
