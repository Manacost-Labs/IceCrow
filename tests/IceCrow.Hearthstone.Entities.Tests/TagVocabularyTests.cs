using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities.Tests;

public sealed class TagVocabularyTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        9,
        6,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Theory]
    [InlineData("MULLIGAN_STATE", "INPUT", GameTag.MulliganState, (int)MulliganState.Input)]
    [InlineData("MULLIGAN_STATE", "DONE", GameTag.MulliganState, (int)MulliganState.Done)]
    [InlineData("STEP", "BEGIN_MULLIGAN", GameTag.Step, (int)GameStep.BeginMulligan)]
    [InlineData("STEP", "FINAL_GAMEOVER", GameTag.Step, (int)GameStep.FinalGameover)]
    [InlineData("STATE", "COMPLETE", GameTag.State, (int)GameLifecycleState.Complete)]
    [InlineData("PREMIUM", "1", GameTag.Premium, 1)]
    [InlineData("PLAYER_LEADERBOARD_PLACE", "3", GameTag.PlayerLeaderboardPlace, 3)]
    public void SymbolicTagsAreStoredWithHearthDbValues(
        string rawTag,
        string rawValue,
        GameTag expectedTag,
        int expectedValue)
    {
        var store = new EntityStore();

        var mutation = store.Apply(new RawTagChanged(
            Timestamp,
            null,
            EntityId: 2,
            EntityName: null,
            rawTag,
            rawValue,
            IsCreationTag: false));

        Assert.Equal(new EntityMutation(2, expectedTag, 0, expectedValue), mutation);
        Assert.Equal(expectedValue, store.Get(2).GetTag(expectedTag));
    }

    [Theory]
    [InlineData("12", GameTag.Premium)]
    [InlineData("19", GameTag.Step)]
    [InlineData("204", GameTag.State)]
    [InlineData("305", GameTag.MulliganState)]
    [InlineData("1373", GameTag.PlayerLeaderboardPlace)]
    public void NumericTagIdsMapToTheSameMembers(string rawTag, GameTag expectedTag)
    {
        var store = new EntityStore();

        var mutation = store.Apply(new RawTagChanged(
            Timestamp,
            null,
            EntityId: 5,
            EntityName: null,
            rawTag,
            "1",
            IsCreationTag: false));

        Assert.Equal(expectedTag, mutation?.Tag);
    }

    [Fact]
    public void UnknownSymbolicStepValuesAreDroppedNotGuessed()
    {
        var store = new EntityStore();

        var mutation = store.Apply(new RawTagChanged(
            Timestamp,
            null,
            EntityId: 1,
            EntityName: null,
            "STEP",
            "MAIN_FUTURE_STEP",
            IsCreationTag: false));

        Assert.Null(mutation);
    }

    [Fact]
    public void GameMetadataEventsDoNotTouchTheStore()
    {
        var store = new EntityStore();

        var mutation = store.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.GameType, "GT_RANKED"));

        Assert.Null(mutation);
        Assert.Equal(0, store.Count);
    }
}
