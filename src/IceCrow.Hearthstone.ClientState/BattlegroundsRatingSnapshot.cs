namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// The Battlegrounds ratings the client currently shows. A null rating means
/// the client did not expose that mode; it is never substituted with a guess.
/// </summary>
public sealed record BattlegroundsRatingSnapshot
{
    public const int MaximumRating = 100_000;

    public BattlegroundsRatingSnapshot(DateTimeOffset observedAt, int? soloRating, int? duosRating)
    {
        ObservedAt = observedAt;
        SoloRating = ClientCardIds.ValidateOptionalRange(soloRating, 0, MaximumRating, nameof(soloRating));
        DuosRating = ClientCardIds.ValidateOptionalRange(duosRating, 0, MaximumRating, nameof(duosRating));
    }

    public DateTimeOffset ObservedAt { get; }

    public int? SoloRating { get; }

    public int? DuosRating { get; }
}
