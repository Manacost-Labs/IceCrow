using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Tests;

/// <summary>
/// Real 2026 client semantics, derived from the first private capture: the
/// game entity and players are referenced by bare name in TAG_CHANGE lines,
/// the raw TURN value maps to Battlegrounds rounds as (raw + 1) / 2, and the
/// solo recruit-to-combat transition is compatibility tag 2022 falling from
/// 1 to 0 on the game entity. Every line is duplicated by a second log
/// source, so all transitions must be idempotent.
/// </summary>
public sealed class RealClientSequenceTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        23,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public void BareNameTurnPhaseAndBoardSemanticsMatchTheRealClient()
    {
        var session = new TrackingSession();
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: 4);
        _ = session.Apply(new GameEntityDeclared(Timestamp, null, EntityId: 10));
        _ = session.Apply(new PlayerEntityDeclared(Timestamp, null, 2, 4, "account-4"));
        _ = session.Apply(new PlayerEntityDeclared(Timestamp, null, 3, 6, "account-6"));
        _ = session.Apply(NumericTag(2, "NEXT_OPPONENT_PLAYER_ID", "6"));
        foreach (var (tag, value) in new[]
                 {
                     ("CARDTYPE", "MINION"),
                     ("ZONE", "PLAY"),
                     ("CONTROLLER", "6"),
                     ("ZONE_POSITION", "1"),
                     ("ATK", "7"),
                     ("HEALTH", "8"),
                 })
        {
            _ = session.Apply(NumericTag(301, tag, value));
        }

        // Raw TURN 1 arrives by bare game-entity name, duplicated by the
        // second log source.
        _ = session.Apply(NamedTag("GameEntity", "TURN", "1"));
        var afterDuplicate = session.Apply(NamedTag("GameEntity", "TURN", "1"));
        Assert.Equal(1, session.Current.Battlegrounds.Turn);
        Assert.Equal(BattlegroundsPhase.Recruit, session.Current.Battlegrounds.Phase);
        Assert.Null(afterDuplicate.EntityMutation);

        // Solo combat transition: tag 2022 set to 1, then dropped to 0.
        _ = session.Apply(NamedTag("GameEntity", "2022", "1"));
        var combatEntry = session.Apply(NamedTag("GameEntity", "2022", "0"));
        Assert.Equal(BattlegroundsPhase.Combat, session.Current.Battlegrounds.Phase);
        var board = Assert.IsType<BoardSnapshot>(combatEntry.ObservedBoard);
        Assert.Equal(6, board.PlayerId);
        Assert.Equal(7, Assert.Single(board.Minions).Attack);

        // The next raw TURN (3 -> round 2) returns the phase to Recruit.
        _ = session.Apply(NamedTag("GameEntity", "TURN", "3"));
        Assert.Equal(2, session.Current.Battlegrounds.Turn);
        Assert.Equal(BattlegroundsPhase.Recruit, session.Current.Battlegrounds.Phase);

        // Second combat captures a second snapshot of the same opponent.
        _ = session.Apply(NamedTag("GameEntity", "2022", "1"));
        _ = session.Apply(NamedTag("GameEntity", "2022", "0"));
        Assert.Equal(BattlegroundsPhase.Combat, session.Current.Battlegrounds.Phase);
        Assert.Equal(
            2,
            session.Current.OpponentMemory.GetHistory(6)?.Snapshots.Count);

        // A descriptor line teaches the local player's name once; the final
        // bare-name terminal playstate then ends the game.
        _ = session.Apply(new RawTagChanged(
            Timestamp,
            null,
            EntityId: 2,
            EntityName: "Player#0000",
            Tag: "1710",
            Value: "1",
            IsCreationTag: false));
        _ = session.Apply(NamedTag("Player#0000", "PLAYSTATE", "LOST"));
        Assert.Equal(BattlegroundsPhase.GameOver, session.Current.Battlegrounds.Phase);
        Assert.False(session.Current.Battlegrounds.IsActive);
        Assert.Equal(0, session.UnresolvedNamedReferenceCount);
    }

    private static RawTagChanged NamedTag(string entityName, string tag, string value) => new(
        Timestamp,
        BlockId: null,
        EntityId: null,
        EntityName: entityName,
        Tag: tag,
        Value: value,
        IsCreationTag: false);

    private static RawTagChanged NumericTag(int entityId, string tag, string value) => new(
        Timestamp,
        BlockId: null,
        EntityId: entityId,
        EntityName: null,
        Tag: tag,
        Value: value,
        IsCreationTag: false);
}
