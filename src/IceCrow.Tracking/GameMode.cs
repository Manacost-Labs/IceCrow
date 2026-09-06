namespace IceCrow.Tracking;

/// <summary>
/// Coarse classification of the client's <c>GameType=</c> token. Only modes
/// IceCrow collects are named; every other token stays <see cref="Other"/> so
/// a new client mode is ignored rather than misfiled.
/// </summary>
public enum GameMode
{
    Unknown,
    Ranked,
    Casual,
    Arena,
    Battlegrounds,
    BattlegroundsDuo,
    Other,
}

public enum ConstructedFormat
{
    Unknown,
    Wild,
    Standard,
    Classic,
    Twist,
}
