using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking;

/// <summary>
/// One board observation per real combat, taken for both sides at once.
/// Combat entry only arms a pending capture — the four 2026-08-31 real
/// captures proved the enemy board is dealt to a fixed opposing-side
/// controller strictly after the phase transition, and that the
/// compatibility phase flip also fires during shopping with no fight at all.
/// The first attack block is the moment the deal is provably complete, so
/// the boards are taken exactly once there; a combat window without any
/// attack (shop residue, or an empty enemy board that never fights) records
/// no observation instead of a false empty board.
/// </summary>
internal sealed class CombatBoardCapture
{
    // Power.log block-type literal for an attack action.
    private const string AttackBlockType = "ATTACK";
    private const int BoardSlots = 7;

    private readonly OpponentMemoryService _opponentMemory;
    private (int? OpponentPlayerId, int Turn)? _pending;

    public CombatBoardCapture(OpponentMemoryService opponentMemory)
    {
        _opponentMemory = opponentMemory;
    }

    /// <summary>The local warband as it entered its most recent fight; survives match end.</summary>
    public BoardSnapshot? LatestLocalBoard { get; private set; }

    public void Reset()
    {
        _pending = null;
        LatestLocalBoard = null;
    }

    /// <summary>Drops a pending capture at match end without forgetting the last board.</summary>
    public void Disarm() => _pending = null;

    public (BoardSnapshot? Opponent, BoardSnapshot? Local) Track(
        BattlegroundsPhase previousPhase,
        BattlegroundsState state,
        GameEvent gameEvent,
        EntityStore entities)
    {
        if (state.Phase != BattlegroundsPhase.Combat)
        {
            _pending = null;
            return default;
        }

        if (previousPhase != BattlegroundsPhase.Combat)
        {
            _pending = (state.CurrentOpponentPlayerId, state.Turn);
            return default;
        }

        if (_pending is not { } pending ||
            gameEvent is not BlockStarted { Block.Type: AttackBlockType } ||
            state.LocalPlayerId is not int localPlayerId)
        {
            return default;
        }

        _pending = null;

        // The client raises a second combat window inside the same round
        // (the raw turn increments mid-fight), whose attacks would overwrite
        // the entering boards with a mid-fight remnant. One observation per
        // round keeps the first (entering) boards.
        if (IsRoundCaptured(pending))
        {
            return default;
        }

        var local = BoardSnapshot.Capture(
            localPlayerId,
            pending.Turn,
            gameEvent.Timestamp,
            SelectBoardSlots(entities.CreateBoardSnapshots(localPlayerId)));
        LatestLocalBoard = local;

        var opponent = pending.OpponentPlayerId is int opponentPlayerId
            ? _opponentMemory.Capture(
                opponentPlayerId,
                pending.Turn,
                entities.CreateOpposingBoardSnapshots(localPlayerId),
                gameEvent.Timestamp)
            : null;
        return (opponent, local);
    }

    private bool IsRoundCaptured((int? OpponentPlayerId, int Turn) pending) =>
        LatestLocalBoard?.Turn == pending.Turn ||
        (pending.OpponentPlayerId is int opponentPlayerId &&
         _opponentMemory.Memory.GetLatest(opponentPlayerId)?.Turn == pending.Turn);

    /// <summary>
    /// Only the seven real board slots count; a slot claimed twice keeps the
    /// newest entity, mirroring the opposing-side rule, so the local board can
    /// never exceed seven minions.
    /// </summary>
    private static IEnumerable<EntitySnapshot> SelectBoardSlots(IReadOnlyList<EntitySnapshot> minions)
    {
        var bySlot = new EntitySnapshot?[BoardSlots + 1];
        foreach (var minion in minions)
        {
            var slot = minion.ZonePosition;
            if (slot is < 1 or > BoardSlots)
            {
                continue;
            }

            if (bySlot[slot] is null || minion.Id > bySlot[slot]!.Id)
            {
                bySlot[slot] = minion;
            }
        }

        return bySlot.OfType<EntitySnapshot>();
    }
}
