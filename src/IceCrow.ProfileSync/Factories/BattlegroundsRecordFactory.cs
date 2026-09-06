using IceCrow.Battlegrounds.Memory;
using IceCrow.ProfileSync.Records;
using IceCrow.Tracking;

namespace IceCrow.ProfileSync.Factories;

/// <summary>
/// Maps a completed Battlegrounds tracking result into the profile record.
/// Certainty flows one way: a tracking grade maps to the same or a lower
/// profile grade, never higher, and a value tracking never observed stays
/// null with <see cref="Certainty.Unknown"/>. MMR has no authoritative
/// source in this slice, so it is always null and Unknown, never estimated.
/// </summary>
public static class BattlegroundsRecordFactory
{
    public static BattlegroundsMatchRecord? Create(TrackingSnapshot snapshot, Guid matchId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionState != TrackingSessionState.Ended ||
            snapshot.Result is not { } result)
        {
            return null;
        }

        return new BattlegroundsMatchRecord(
            MatchId: matchId,
            Mode: MapMode(result.Metadata.Mode),
            MmrBefore: null,
            MmrAfter: null,
            MmrConfidence: Certainty.Unknown,
            HeroCardId: BoundedCardId(result.HeroCardId),
            Placement: result.Placement,
            PlacementConfidence: result.Placement is null
                ? Certainty.Unknown
                : MapCertainty(result.PlacementCertainty),
            StartedAt: result.StartedAt,
            EndedAt: result.EndedAt,
            DurationSeconds: ClampDuration(result.StartedAt, result.EndedAt),
            FinalTurn: ClampTurn(result.FinalTurn),
            FinalBoard: MapFinalBoard(result.FinalBoard, result.FinalBoardCertainty),
            HearthstoneBuild: result.Metadata.BuildNumber);
    }

    private static BattlegroundsMode MapMode(GameMode mode) => mode switch
    {
        GameMode.Battlegrounds => BattlegroundsMode.Solo,
        GameMode.BattlegroundsDuo => BattlegroundsMode.Duos,
        _ => BattlegroundsMode.Unknown,
    };

    /// <summary>Same grade or lower; a grade this mapping does not know is Unknown.</summary>
    private static Certainty MapCertainty(EvidenceCertainty certainty) => certainty switch
    {
        EvidenceCertainty.Exact => Certainty.Exact,
        EvidenceCertainty.Partial => Certainty.Partial,
        EvidenceCertainty.Inferred => Certainty.Inferred,
        _ => Certainty.Unknown,
    };

    private static FinalBoardRecord? MapFinalBoard(BoardSnapshot? board, EvidenceCertainty certainty)
    {
        if (board is null)
        {
            return null;
        }

        var minions = board.Minions
            .Take(ProfileRecordLimits.MaximumBoardMinions)
            .Select(static minion => new FinalBoardMinion(
                minion.ZonePosition,
                BoundedCardId(minion.CardId),
                minion.Attack,
                minion.Health,
                minion.IsGolden))
            .ToArray();
        return new FinalBoardRecord(
            board.Timestamp,
            ClampTurn(board.Turn),
            minions,
            MapCertainty(certainty));
    }

    private static int ClampDuration(DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        (int)Math.Clamp(
            (endedAt - startedAt).TotalSeconds,
            0,
            ProfileRecordLimits.MaximumDurationSeconds);

    private static int ClampTurn(int turn) =>
        Math.Clamp(turn, 0, ProfileRecordLimits.MaximumTurns);

    /// <summary>An over-long card id is dropped, never truncated into a different id.</summary>
    private static string? BoundedCardId(string? cardId) =>
        cardId is { Length: > 0 and <= ProfileRecordLimits.MaximumCardIdLength } ? cardId : null;
}
