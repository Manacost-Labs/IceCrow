namespace IceCrow.ProfileSync.Records;

public static class ProfileRecordLimits
{
    public const int MaximumCardIdLength = 64;
    public const int MaximumMulliganCards = 10;
    public const int MaximumObservedOpponentCards = 64;
    public const int MaximumBoardMinions = 7;
    public const int MaximumArenaOffers = 8;
    public const int MaximumArenaPicks = 40;
    public const int MaximumCollectionCards = 20_000;
    public const int MaximumTurns = 200;
    public const int MaximumDurationSeconds = 6 * 60 * 60;
}
