namespace IceCrow.Infrastructure.ManacostApi;

public sealed record ManacostDataStatus(
    bool CacheReady,
    bool OfflineMode,
    string? DataVersion,
    string? HearthstoneBuild,
    DateTimeOffset? LastSync,
    int CardCount,
    int BattlegroundsMinionCount,
    int BattlegroundsHeroCount,
    string? SyncError);
