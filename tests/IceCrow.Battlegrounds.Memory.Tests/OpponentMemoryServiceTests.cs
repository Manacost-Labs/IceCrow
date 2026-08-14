using System.Globalization;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Battlegrounds.Memory.Tests;

public sealed class OpponentMemoryServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void FirstFightStoresLatestBoardAndHistory()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 3, health: 4);
        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 1);

        var latest = Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2));
        var history = Assert.IsType<OpponentBoardHistory>(service.Memory.GetHistory(2));
        Assert.Equal(2, latest.PlayerId);
        Assert.Equal(1, latest.Turn);
        Assert.Equal(Timestamp, latest.Timestamp);
        Assert.Single(latest.Minions);
        Assert.Single(history.Snapshots);
        Assert.Same(latest, history.Latest);
    }

    [Fact]
    public void SnapshotCapturesSevenMinionsOrderedByZonePosition()
    {
        var fixture = new EntityFixture();
        foreach (var position in Enumerable.Range(1, 7).Reverse())
        {
            fixture.AddMinion(
                100 + position,
                playerId: 2,
                zonePosition: position,
                attack: position,
                health: position + 1);
        }

        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 3);

        var board = Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2));
        Assert.Equal(7, board.Minions.Count);
        Assert.Equal(
            Enumerable.Range(1, 7),
            board.Minions.Select(static minion => minion.ZonePosition));
    }

    [Fact]
    public void SnapshotIncludesOnlyOpponentMinionsInPlay()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 3, health: 4);
        fixture.AddMinion(102, playerId: 1, zonePosition: 2, attack: 5, health: 6);
        fixture.AddMinion(103, playerId: 2, zonePosition: 3, attack: 7, health: 8);
        fixture.SetTag(103, "ZONE", "HAND");

        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 3);

        var board = Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2));
        Assert.Equal(101, Assert.Single(board.Minions).EntityId);
    }

    [Fact]
    public void EmptyOpponentBoardIsRememberedAsObserved()
    {
        var fixture = new EntityFixture();

        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 2);

        var board = Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2));
        Assert.Empty(board.Minions);
    }

    [Fact]
    public void SubsequentFightReplacesLatestSnapshot()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 2, health: 3);
        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 1);
        var first = service.Memory.GetLatest(2);

        fixture.SetTag(101, "ATK", "8");
        _ = service.Capture(2, 2, fixture.Snapshots, Timestamp.AddMinutes(2));

        var latest = Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2));
        Assert.NotSame(first, latest);
        Assert.Equal(2, latest.Turn);
        Assert.Equal(8, Assert.Single(latest.Minions).Attack);
    }

    [Fact]
    public void HistoryPreservesPreviousSnapshots()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 2, health: 3);
        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 1);
        var first = service.Memory.GetLatest(2);

        fixture.SetTag(101, "ATK", "8");
        _ = service.Capture(2, 2, fixture.Snapshots, Timestamp.AddMinutes(2));

        var history = Assert.IsType<OpponentBoardHistory>(service.Memory.GetHistory(2));
        Assert.Equal(2, history.Snapshots.Count);
        Assert.Same(first, history.Snapshots[0]);
        Assert.Equal(2, Assert.Single(history.Snapshots[0].Minions).Attack);
        Assert.Equal(8, Assert.Single(history.Snapshots[1].Minions).Attack);
    }

    [Fact]
    public void CapturedSnapshotsDoNotReferenceMutableEntities()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 2, health: 6);
        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 1);
        var captured = Assert.Single(
            Assert.IsType<BoardSnapshot>(service.Memory.GetLatest(2)).Minions);

        fixture.SetTag(101, "ATK", "10");
        fixture.SetTag(101, "DAMAGE", "4");

        Assert.Equal(2, captured.Attack);
        Assert.Equal(6, captured.Health);
        Assert.Equal(2, captured.Tags[GameTag.Attack]);
    }

    [Fact]
    public void NewMatchClearsOpponentHistory()
    {
        var fixture = new EntityFixture();
        fixture.AddMinion(101, playerId: 2, zonePosition: 1, attack: 2, health: 3);
        var service = CaptureBoard(fixture, opponentPlayerId: 2, turn: 1);
        Assert.NotNull(service.Memory.GetLatest(2));

        service.Reset();

        Assert.Empty(service.Memory.Histories);
    }

    [Fact]
    public void NeverFoughtPlayerHasNoSnapshot()
    {
        var service = new OpponentMemoryService();

        Assert.Null(service.Memory.GetLatest(3));
        Assert.Null(service.Memory.GetHistory(3));
    }

    [Fact]
    public void SnapshotHistoryAndOpponentCountRemainBounded()
    {
        var service = new OpponentMemoryService(
            maximumOpponents: 2,
            maximumSnapshotsPerOpponent: 3);

        for (var turn = 1; turn <= 6; turn++)
        {
            _ = service.Capture(2, turn, [], Timestamp.AddMinutes(turn));
        }

        _ = service.Capture(3, 1, [], Timestamp);
        _ = service.Capture(4, 1, [], Timestamp);

        var history = Assert.IsType<OpponentBoardHistory>(service.Memory.GetHistory(2));
        Assert.Equal([4, 5, 6], history.Snapshots.Select(static snapshot => snapshot.Turn));
        Assert.Equal(2, service.Memory.Histories.Count);
        Assert.Null(service.Memory.GetHistory(4));
        Assert.Equal(4, service.SnapshotCount);
        Assert.Equal(3, service.MaximumSnapshotCount);
    }

    private static OpponentMemoryService CaptureBoard(
        EntityFixture fixture,
        int opponentPlayerId,
        int turn)
    {
        var service = new OpponentMemoryService();
        _ = service.Capture(
            opponentPlayerId,
            turn,
            fixture.Snapshots,
            Timestamp);
        return service;
    }

    private sealed class EntityFixture
    {
        private readonly EntityStore _store = new();

        public IReadOnlyList<EntitySnapshot> Snapshots => _store.CreateAllSnapshots();

        public void AddMinion(
            int entityId,
            int playerId,
            int zonePosition,
            int attack,
            int health)
        {
            SetTag(entityId, "CARDTYPE", "MINION");
            SetTag(entityId, "ZONE", "PLAY");
            SetTag(entityId, "CONTROLLER", playerId.ToString(CultureInfo.InvariantCulture));
            SetTag(entityId, "ZONE_POSITION", zonePosition.ToString(CultureInfo.InvariantCulture));
            SetTag(entityId, "ATK", attack.ToString(CultureInfo.InvariantCulture));
            SetTag(entityId, "HEALTH", health.ToString(CultureInfo.InvariantCulture));
            _ = _store.Apply(
                new EntityRevealed(
                    Timestamp,
                    BlockId: null,
                    EntityId: entityId,
                    EntityName: $"Minion {entityId}",
                    CardId: $"MINION_{entityId}"));
        }

        public void SetTag(int entityId, string tag, string value) =>
            _ = _store.Apply(
                new RawTagChanged(
                    Timestamp,
                    BlockId: null,
                    EntityId: entityId,
                    EntityName: null,
                    Tag: tag,
                    Value: value,
                    IsCreationTag: false));
    }
}
