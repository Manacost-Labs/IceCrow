namespace IceCrow.Battlegrounds;

public sealed record BattlegroundsState(
    bool IsActive,
    int Turn,
    BattlegroundsPhase Phase,
    int? LocalPlayerId,
    int? CurrentOpponentPlayerId,
    LobbyState Lobby)
{
    public static BattlegroundsState Empty { get; } = new(
        IsActive: false,
        Turn: 0,
        Phase: BattlegroundsPhase.Unknown,
        LocalPlayerId: null,
        CurrentOpponentPlayerId: null,
        Lobby: LobbyState.Empty);
}
