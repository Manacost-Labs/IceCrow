using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;

namespace IceCrow.Tracking;

public sealed record TrackingUpdate(
    long Revision,
    TrackingSessionState PreviousSessionState,
    TrackingSessionState SessionState,
    BattlegroundsPhase PreviousPhase,
    BattlegroundsState Battlegrounds,
    EntityMutation? EntityMutation,
    EntitySnapshot? Entity,
    BoardSnapshot? ObservedBoard)
{
    public bool EnteredCombat =>
        PreviousPhase != BattlegroundsPhase.Combat &&
        Battlegrounds.Phase == BattlegroundsPhase.Combat;
}
