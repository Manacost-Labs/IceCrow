using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds.Memory;

public sealed class OpponentMemoryService
{
    private bool _matchActive;
    private BattlegroundsPhase _previousPhase = BattlegroundsPhase.Unknown;

    public OpponentMemory Memory { get; private set; } = OpponentMemory.Empty;

    public BoardSnapshot? Update(
        BattlegroundsState state,
        IEnumerable<EntitySnapshot> entities,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(entities);

        if (!state.IsActive)
        {
            _matchActive = false;
            _previousPhase = state.Phase;
            return null;
        }

        if (!_matchActive)
        {
            Memory = OpponentMemory.Empty;
            _matchActive = true;
            _previousPhase = BattlegroundsPhase.Unknown;
        }

        BoardSnapshot? captured = null;
        if (_previousPhase != BattlegroundsPhase.Combat &&
            state.Phase == BattlegroundsPhase.Combat &&
            state.CurrentOpponentPlayerId is int opponentPlayerId)
        {
            captured = BoardSnapshot.Capture(
                opponentPlayerId,
                state.Turn,
                timestamp,
                entities);
            Memory = Memory.Remember(captured);
        }

        _previousPhase = state.Phase;
        return captured;
    }

    public void Reset()
    {
        Memory = OpponentMemory.Empty;
        _matchActive = false;
        _previousPhase = BattlegroundsPhase.Unknown;
    }
}
