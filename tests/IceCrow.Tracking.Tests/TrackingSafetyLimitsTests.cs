using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Tests;

public sealed class TrackingSafetyLimitsTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void DistinctNumericTagsStopAtThePerEntityHardLimit()
    {
        var session = new TrackingSession(new TrackingSessionLimits(
            maximumTagsPerEntity: 2,
            tagsPerEntityWarningThreshold: 1));
        _ = session.StartBattlegroundsMatch(Timestamp);

        _ = ApplyTag(session, 1, "1000", "1");
        _ = ApplyTag(session, 1, "1001", "1");
        var exception = Assert.Throws<TrackingSafetyLimitExceededException>(
            () => ApplyTag(session, 1, "1002", "1"));

        Assert.Equal(TrackingSafetyLimit.TagsPerEntity, exception.Limit);
        Assert.Equal(2, session.TagCount);
        Assert.Equal(2, session.MaximumTagsOnEntity);
    }

    [Fact]
    public void LobbyPlayerHardLimitRejectsOnlyTheNewPlayer()
    {
        var session = new TrackingSession(new TrackingSessionLimits(
            maximumLobbyPlayers: 1,
            lobbyPlayerWarningThreshold: 1));
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: 1);
        _ = ApplyTag(session, 1, "PLAYER_ID", "1");

        var exception = Assert.Throws<TrackingSafetyLimitExceededException>(
            () => ApplyTag(session, 2, "PLAYER_ID", "2"));

        Assert.Equal(TrackingSafetyLimit.LobbyPlayers, exception.Limit);
        Assert.Equal(1, session.Current.Battlegrounds.Lobby.Count);
        Assert.False(session.ContainsEntity(2));
    }

    [Fact]
    public void TotalTagHardLimitBoundsTagsAcrossManyEntities()
    {
        var session = new TrackingSession(new TrackingSessionLimits(
            maximumTagsPerEntity: 4,
            tagsPerEntityWarningThreshold: 3,
            maximumTotalTags: 2,
            totalTagWarningThreshold: 1));
        _ = session.StartBattlegroundsMatch(Timestamp);

        _ = ApplyTag(session, 1, "1000", "1");
        _ = ApplyTag(session, 2, "1001", "1");
        var exception = Assert.Throws<TrackingSafetyLimitExceededException>(
            () => ApplyTag(session, 3, "1002", "1"));

        Assert.Equal(TrackingSafetyLimit.TotalTags, exception.Limit);
        Assert.Equal(2, session.TagCount);
        Assert.False(session.ContainsEntity(3));
    }

    private static TrackingUpdate ApplyTag(
        TrackingSession session,
        int entityId,
        string tag,
        string value) => session.Apply(new RawTagChanged(
            Timestamp,
            BlockId: null,
            EntityId: entityId,
            EntityName: null,
            Tag: tag,
            Value: value,
            IsCreationTag: false));
}
