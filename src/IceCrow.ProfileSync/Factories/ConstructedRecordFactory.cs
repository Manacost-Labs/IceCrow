using IceCrow.ProfileSync.Records;
using IceCrow.Hearthstone.ClientState;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.ProfileSync.Factories;

/// <summary>
/// Maps a completed <see cref="ConstructedMatchSummary"/> onto the profile
/// record contracts. Certainty goes one way through
/// <see cref="EvidenceCertaintyMapping"/>. Optional own-deck evidence is
/// accepted only when it was observed no later than match start and its format
/// agrees with the game; a game handle is never approximated.
/// </summary>
public static class ConstructedRecordFactory
{
    public static ConstructedMatchRecord? CreateRanked(
        ConstructedMatchSummary summary,
        Guid matchId,
        SelectedDeckSnapshot? selectedDeck = null,
        Certainty deckAssociationConfidence = Certainty.Unknown)
    {
        ArgumentNullException.ThrowIfNull(summary);
        RequireMatchId(matchId);
        var format = summary.Mode == GameMode.Ranked
            ? summary.Format switch
            {
                ConstructedFormat.Standard => "standard",
                ConstructedFormat.Wild => "wild",
                _ => null,
            }
            : null;
        if (format is null)
        {
            return null;
        }

        var observed = CardIds(summary.ObservedOpponentCards, ProfileRecordLimits.MaximumObservedOpponentCards);
        return new ConstructedMatchRecord(
            matchId,
            "ranked",
            format,
            Result(summary.Result),
            EvidenceCertaintyMapping.ToCertainty(summary.ResultCertainty),
            summary.StartedAt,
            summary.EndedAt,
            DurationSeconds(summary),
            Turns(summary.Turns),
            CardId(summary.PlayerHeroCardId),
            CardId(summary.OpponentHeroCardId),
            PlayerDeck(summary, selectedDeck, deckAssociationConfidence),
            Mulligan(summary.Mulligan),
            ReplacedCount(summary.OpponentMulliganReplacedCount),
            new OpponentDeckEvidence(
                observed,
                DeckCode: null,
                DeckHash: null,
                observed.Count > 0 ? Certainty.Partial : Certainty.Unknown),
            GameJoinEvidence: null,
            summary.HearthstoneBuild,
            summary.ScenarioId);
    }

    private static DeckEvidence PlayerDeck(
        ConstructedMatchSummary summary,
        SelectedDeckSnapshot? selectedDeck,
        Certainty associationConfidence)
    {
        var expectedFormat = summary.Format switch
        {
            ConstructedFormat.Standard => "standard",
            ConstructedFormat.Wild => "wild",
            _ => null,
        };
        if (selectedDeck is null ||
            expectedFormat is null ||
            associationConfidence is not (Certainty.Inferred or Certainty.Partial or Certainty.Exact) ||
            selectedDeck.ObservedAt > summary.StartedAt ||
            !string.Equals(selectedDeck.FormatToken, expectedFormat, StringComparison.OrdinalIgnoreCase))
        {
            return DeckEvidence.Unknown;
        }

        var deckCode = selectedDeck.DeckCode;
        var deckHash = selectedDeck.CardIds.Count > 0
            ? DeckHash.Compute(selectedDeck.CardIds)
            : null;
        return deckCode is null && deckHash is null
            ? DeckEvidence.Unknown
            : new DeckEvidence(deckCode, deckHash, associationConfidence);
    }

    public static ArenaMatchRecord? CreateArena(ConstructedMatchSummary summary, Guid matchId)
    {
        ArgumentNullException.ThrowIfNull(summary);
        RequireMatchId(matchId);
        if (summary.Mode != GameMode.Arena)
        {
            return null;
        }

        return new ArenaMatchRecord(
            matchId,
            RunId: null,
            ScoreBefore: null,
            ScoreAfter: null,
            Certainty.Unknown,
            Result(summary.Result),
            EvidenceCertaintyMapping.ToCertainty(summary.ResultCertainty),
            CardId(summary.PlayerHeroCardId),
            CardId(summary.OpponentHeroCardId),
            summary.StartedAt,
            summary.EndedAt,
            DurationSeconds(summary),
            Turns(summary.Turns),
            Mulligan(summary.Mulligan),
            ReplacedCount(summary.OpponentMulliganReplacedCount),
            summary.HearthstoneBuild);
    }

    private static void RequireMatchId(Guid matchId)
    {
        if (matchId == Guid.Empty)
        {
            throw new ArgumentException("A profile match record needs a non-empty match id.", nameof(matchId));
        }
    }

    private static MatchResult Result(ConstructedMatchResult result) => result switch
    {
        ConstructedMatchResult.Won => MatchResult.Won,
        ConstructedMatchResult.Lost => MatchResult.Lost,
        ConstructedMatchResult.Tied => MatchResult.Tied,
        _ => MatchResult.Unknown,
    };

    private static MulliganRecord Mulligan(ConstructedMulligan mulligan)
    {
        var confidence = EvidenceCertaintyMapping.ToCertainty(mulligan.Certainty);
        if (confidence == Certainty.Unknown)
        {
            return MulliganRecord.Unknown;
        }

        return new MulliganRecord(
            CardIds(mulligan.Initial, ProfileRecordLimits.MaximumMulliganCards),
            CardIds(mulligan.Kept, ProfileRecordLimits.MaximumMulliganCards),
            CardIds(mulligan.Replaced, ProfileRecordLimits.MaximumMulliganCards),
            CardIds(mulligan.After, ProfileRecordLimits.MaximumMulliganCards),
            confidence);
    }

    private static int DurationSeconds(ConstructedMatchSummary summary)
    {
        var seconds = (summary.EndedAt - summary.StartedAt).TotalSeconds;
        return (int)Math.Clamp(seconds, 0, ProfileRecordLimits.MaximumDurationSeconds);
    }

    private static int Turns(int turns) => Math.Clamp(turns, 0, ProfileRecordLimits.MaximumTurns);

    private static int? ReplacedCount(int? count) => count is >= 0 ? count : null;

    private static string? CardId(string? cardId) =>
        cardId is { Length: > 0 and <= ProfileRecordLimits.MaximumCardIdLength } ? cardId : null;

    private static List<string> CardIds(IReadOnlyList<string> cardIds, int maximum)
    {
        var accepted = new List<string>(Math.Min(cardIds.Count, maximum));
        foreach (var cardId in cardIds)
        {
            if (accepted.Count >= maximum)
            {
                break;
            }

            if (CardId(cardId) is { } valid)
            {
                accepted.Add(valid);
            }
        }

        return accepted;
    }
}
