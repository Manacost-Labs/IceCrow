namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Links a Power.log Arena match to the client-state run it was played in.
/// Scores are win counts: a loss leaves <see cref="ScoreAfter"/> equal to
/// <see cref="ScoreBefore"/>. <see cref="Confidence"/> is Exact only when the
/// run score advanced by exactly one win or one loss across the match,
/// Partial when only one side was observed, and Unknown otherwise.
/// </summary>
public sealed record ArenaMatchAssociation(
    Guid? RunId,
    int? ScoreBefore,
    int? ScoreAfter,
    Certainty Confidence)
{
    public static readonly ArenaMatchAssociation Unknown = new(null, null, null, Certainty.Unknown);
}
