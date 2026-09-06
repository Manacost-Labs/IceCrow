namespace IceCrow.ProfileSync;

/// <summary>Wire discriminators shared with the HearthPulse ingestion contract.</summary>
public static class ProfileEventType
{
    public const string ConstructedMatch = "constructed_match";
    public const string ArenaMatch = "arena_match";
    public const string ArenaRun = "arena_run";
    public const string ArenaDraftPick = "arena_draft_pick";
    public const string BattlegroundsMatch = "battlegrounds_match";
    public const string CollectionSnapshot = "collection_snapshot";

    public static readonly IReadOnlyList<string> All =
    [
        ConstructedMatch,
        ArenaMatch,
        ArenaRun,
        ArenaDraftPick,
        BattlegroundsMatch,
        CollectionSnapshot,
    ];

    public static bool IsKnown(string? type) =>
        type is not null && All.Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// State-like events where only the newest pending item matters; history
    /// events are never treated this way.
    /// </summary>
    public static bool IsLatestOnly(string type) =>
        string.Equals(type, CollectionSnapshot, StringComparison.Ordinal);
}
