using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Protocol.Tests.Fixtures;

public static class PowerProtocolFixtures
{
    public static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        12,
        30,
        0,
        TimeSpan.Zero);

    public static IEnumerable<object[]> SupportedSingleLineEvents()
    {
        yield return
        [
            "PowerTaskList.DebugPrintPower() - GameEntity EntityID=1",
            new GameEntityDeclared(Timestamp, BlockId: null, EntityId: 1),
        ];
        yield return
        [
            "GameState.DebugPrintPower() - Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115188075855872 lo=42]",
            new PlayerEntityDeclared(
                Timestamp,
                BlockId: null,
                EntityId: 2,
                PlayerId: 1,
                GameAccountId: "[hi=144115188075855872 lo=42]"),
        ];
        yield return
        [
            "TAG_CHANGE Entity=64 tag=ZONE value=HAND",
            new RawTagChanged(
                Timestamp,
                BlockId: null,
                EntityId: 64,
                EntityName: null,
                Tag: "ZONE",
                Value: "HAND",
                IsCreationTag: false),
        ];
        yield return
        [
            "TAG_CHANGE Entity=Hero A tag=CURRENT_PLAYER value=1",
            new RawTagChanged(
                Timestamp,
                BlockId: null,
                EntityId: null,
                EntityName: "Hero A",
                Tag: "CURRENT_PLAYER",
                Value: "1",
                IsCreationTag: false),
        ];
        yield return
        [
            "FULL_ENTITY - Creating ID=221 CardID=",
            new EntityCreated(Timestamp, BlockId: null, EntityId: 221, CardId: string.Empty),
        ];
        yield return
        [
            "FULL_ENTITY - Updating [name=Silver Hand Recruit id=68 zone=PLAY zonePos=1 cardId=CS2_101t player=1] CardID=CS2_101t",
            new EntityCreated(Timestamp, BlockId: null, EntityId: 68, CardId: "CS2_101t"),
        ];
        yield return
        [
            "SHOW_ENTITY - Updating Entity=218 CardID=MEND_504e",
            new EntityRevealed(
                Timestamp,
                BlockId: null,
                EntityId: 218,
                EntityName: null,
                CardId: "MEND_504e"),
        ];
        yield return
        [
            "CHANGE_ENTITY - Updating Entity=[name=Shifter Zerus id=77 zone=HAND zonePos=3 cardId=OG_123 player=1] CardID=NEW_001",
            new EntityChanged(
                Timestamp,
                BlockId: null,
                EntityId: 77,
                EntityName: "Shifter Zerus",
                CardId: "NEW_001"),
        ];
    }
}
