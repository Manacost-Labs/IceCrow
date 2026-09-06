namespace IceCrow.Battlegrounds;

/// <summary>
/// <c>LocalPlacement</c> is the local hero's PLAYER_LEADERBOARD_PLACE as the
/// client last printed it: the live leaderboard row while the game runs and
/// the final placement once the local player is out. It stays null until the
/// tag was observed on an entity that belongs to the local player.
/// </summary>
public sealed record BattlegroundsState(
    bool IsActive,
    int Turn,
    BattlegroundsPhase Phase,
    int? LocalPlayerId,
    int? CurrentOpponentPlayerId,
    LobbyState Lobby,
    int? LocalPlacement = null)
{
    public static BattlegroundsState Empty { get; } = new(
        IsActive: false,
        Turn: 0,
        Phase: BattlegroundsPhase.Unknown,
        LocalPlayerId: null,
        CurrentOpponentPlayerId: null,
        Lobby: LobbyState.Empty);
}
