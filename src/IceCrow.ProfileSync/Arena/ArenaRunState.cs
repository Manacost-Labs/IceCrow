using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Mutable state of the one open Arena run: identity, ratings, the score
/// history used to associate Power.log matches, and the last emitted run
/// record key so unchanged scores are not re-sent.
/// </summary>
internal sealed class ArenaRunState
{
    public const int MaximumScoreObservations = 64;

    private readonly List<ArenaScoreObservation> _scores = new(MaximumScoreObservations);
    private (int Wins, int Losses, bool IsComplete)? _lastEmitted;

    public ArenaRunState(Guid runId, ArenaClientSnapshot first)
    {
        RunId = runId;
        RunKey = first.RunKey;
        StartedAt = first.ObservedAt;
    }

    public Guid RunId { get; }

    public string? RunKey { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? HeroCardId { get; private set; }

    public int? RatingBefore { get; private set; }

    public bool IsDrafting { get; private set; }

    public ArenaDraftTracker Draft { get; } = new();

    public ArenaDraftPickRecord? ObservePick(ArenaClientSnapshot snapshot) => Draft.Observe(RunId, snapshot);

    public ArenaRunRecord? ObserveRun(ArenaClientSnapshot snapshot)
    {
        RunKey ??= snapshot.RunKey;
        HeroCardId ??= snapshot.HeroCardId;
        IsDrafting = snapshot.IsDrafting;
        var isComplete = snapshot.IsRunComplete ?? false;
        if (!isComplete)
        {
            RatingBefore ??= snapshot.Rating;
        }
        else
        {
            EndedAt ??= snapshot.ObservedAt;
        }

        RecordScore(snapshot);
        if (snapshot.IsDrafting || snapshot.DeckCardIds.Count == 0)
        {
            return null;
        }

        var key = (snapshot.Wins, snapshot.Losses, isComplete);
        if (_lastEmitted == key)
        {
            return null;
        }

        _lastEmitted = key;
        var ratingAfter = isComplete ? snapshot.Rating : null;
        return new ArenaRunRecord(
            RunId,
            snapshot.HeroCardId ?? HeroCardId,
            BuildDeckEvidence(snapshot),
            snapshot.DeckCardIds,
            snapshot.Wins,
            snapshot.Losses,
            Certainty.Exact,
            StartedAt,
            isComplete ? EndedAt : null,
            isComplete,
            RatingBefore,
            ratingAfter,
            RatingBefore is not null && ratingAfter is not null ? Certainty.Exact : Certainty.Unknown);
    }

    /// <summary>
    /// The score in effect at the match start is the last change observed at or
    /// before it; the match outcome is the first score change observed after
    /// the match began, which in practice arrives just after its end.
    /// </summary>
    public ArenaMatchAssociation Associate(DateTimeOffset matchStartedAt, DateTimeOffset matchEndedAt)
    {
        if (matchStartedAt < StartedAt || (EndedAt is { } endedAt && matchStartedAt > endedAt))
        {
            return ArenaMatchAssociation.Unknown;
        }

        ArenaScoreObservation? before = null;
        ArenaScoreObservation? after = null;
        foreach (var score in _scores)
        {
            if (score.ObservedAt <= matchStartedAt)
            {
                before = score;
            }
            else if (after is null)
            {
                after = score;
            }
        }

        if (before is { } b && after is { } a)
        {
            var advanced = a.Wins - b.Wins + (a.Losses - b.Losses);
            var exact = advanced == 1 && a.Wins >= b.Wins && a.Losses >= b.Losses;
            return exact
                ? new ArenaMatchAssociation(RunId, b.Wins, a.Wins, Certainty.Exact)
                : new ArenaMatchAssociation(RunId, null, null, Certainty.Unknown);
        }

        return before is null && after is null
            ? new ArenaMatchAssociation(RunId, null, null, Certainty.Unknown)
            : new ArenaMatchAssociation(RunId, before?.Wins, after?.Wins, Certainty.Partial);
    }

    private void RecordScore(ArenaClientSnapshot snapshot)
    {
        if (_scores.Count > 0)
        {
            var last = _scores[^1];
            if (last.Wins == snapshot.Wins && last.Losses == snapshot.Losses)
            {
                return;
            }
        }

        if (_scores.Count == MaximumScoreObservations)
        {
            _scores.RemoveAt(0);
        }

        _scores.Add(new ArenaScoreObservation(snapshot.ObservedAt, snapshot.Wins, snapshot.Losses));
    }

    private static DeckEvidence BuildDeckEvidence(ArenaClientSnapshot snapshot)
    {
        var confidence = snapshot.DeckCode is not null || snapshot.DeckCardIds.Count == 30
            ? Certainty.Exact
            : Certainty.Partial;
        return new DeckEvidence(snapshot.DeckCode, DeckHash.Compute(snapshot.DeckCardIds), confidence);
    }

    private readonly record struct ArenaScoreObservation(DateTimeOffset ObservedAt, int Wins, int Losses);
}
