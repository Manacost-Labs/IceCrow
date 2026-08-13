using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests.Fixtures;

internal static class DeterministicMatchFixture
{
    private static readonly DateTimeOffset StartedAt = new(
        2026,
        8,
        13,
        21,
        0,
        0,
        TimeSpan.Zero);

    public static RecordedMatch Create()
    {
        var recorder = new MatchRecorder(StartedAt);
        var sequence = 0;
        recorder.RecordMatchStarted(NextTimestamp(), localPlayerId: 1);

        Tag(1, "CARDTYPE", "PLAYER");
        Tag(1, "PLAYER_ID", "1");
        Tag(1, "CURRENT_PLAYER", "1");
        Tag(1, "HERO_ENTITY", "101");
        Tag(1, "NEXT_OPPONENT_PLAYER_ID", "2");
        Tag(1, "PLAYSTATE", "1");

        Tag(2, "CARDTYPE", "PLAYER");
        Tag(2, "PLAYER_ID", "2");
        Tag(2, "HERO_ENTITY", "102");
        Tag(2, "PLAYSTATE", "1");

        Tag(101, "CARDTYPE", "HERO");
        Tag(101, "PLAYER_ID", "1");
        Tag(101, "HEALTH", "40");
        Tag(101, "PLAYER_TECH_LEVEL", "4");
        Reveal(101, "Reno", "TB_BaconShop_HERO_41");

        Tag(102, "CARDTYPE", "HERO");
        Tag(102, "PLAYER_ID", "2");
        Tag(102, "HEALTH", "40");
        Tag(102, "PLAYER_TECH_LEVEL", "5");
        Reveal(102, "Millhouse", "TB_BaconShop_HERO_49");

        foreach (var position in Enumerable.Range(1, 7))
        {
            var entityId = 200 + position;
            Tag(entityId, "CARDTYPE", "MINION");
            Tag(entityId, "ZONE", "PLAY");
            Tag(entityId, "CONTROLLER", "2");
            Tag(entityId, "ZONE_POSITION", position.ToString(CultureInfo.InvariantCulture));
            Tag(entityId, "ATK", (position + 3).ToString(CultureInfo.InvariantCulture));
            Tag(entityId, "HEALTH", (position + 5).ToString(CultureInfo.InvariantCulture));
            Reveal(entityId, $"Minion {position}", $"BG_MINION_{position}");
        }

        Tag(500, "TURN", "15");
        _ = recorder.AddCheckpoint("turn-8");

        Tag(500, "2022", "1");
        Tag(500, "2022", "0");
        _ = recorder.AddCheckpoint("combat");

        recorder.RecordMatchEnded(NextTimestamp());
        _ = recorder.AddCheckpoint("game-over");
        return recorder.CreateMatch();

        DateTimeOffset NextTimestamp() => StartedAt.AddMilliseconds(sequence++);

        void Tag(int entityId, string tag, string value) => recorder.Record(
            new RawTagChanged(
                NextTimestamp(),
                BlockId: null,
                EntityId: entityId,
                EntityName: null,
                Tag: tag,
                Value: value,
                IsCreationTag: false));

        void Reveal(int entityId, string name, string cardId) => recorder.Record(
            new EntityRevealed(
                NextTimestamp(),
                BlockId: null,
                EntityId: entityId,
                EntityName: name,
                CardId: cardId));
    }
}
