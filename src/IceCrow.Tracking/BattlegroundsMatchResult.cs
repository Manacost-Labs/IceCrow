using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;

namespace IceCrow.Tracking;

/// <summary>
/// The frozen outcome of one Battlegrounds match, created exactly once when
/// the session ends. Every fact carries the certainty of the evidence that
/// produced it; a value the client never printed stays null and Unknown.
/// </summary>
public sealed record BattlegroundsMatchResult(
    int? Placement,
    EvidenceCertainty PlacementCertainty,
    string? HeroCardId,
    int FinalTurn,
    BoardSnapshot? FinalBoard,
    EvidenceCertainty FinalBoardCertainty,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    GameMetadataState Metadata)
{
    /// <summary>Eight heroes in a solo lobby; no higher final place exists.</summary>
    public const int MaximumSoloPlacement = 8;

    /// <summary>Four teams in a Duos lobby; the place counts teams, not heroes.</summary>
    public const int MaximumDuosPlacement = 4;

    public static BattlegroundsMatchResult Create(
        BattlegroundsState battlegrounds,
        BoardSnapshot? latestLocalBoard,
        GameMetadataState metadata,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(battlegrounds);
        ArgumentNullException.ThrowIfNull(metadata);

        var heroCardId = battlegrounds.LocalPlayerId is int localPlayerId
            ? battlegrounds.Lobby.GetPlayer(localPlayerId)?.HeroCardId
            : null;

        return new BattlegroundsMatchResult(
            battlegrounds.LocalPlacement,
            GradePlacement(battlegrounds.LocalPlacement, metadata.Mode),
            heroCardId,
            battlegrounds.Turn,
            latestLocalBoard,
            GradeFinalBoard(latestLocalBoard, battlegrounds.Turn),
            startedAt,
            endedAt,
            metadata);
    }

    /// <summary>
    /// A place the client printed inside the lobby range is exact. A place
    /// beyond the last slot of the mode means the tag does not carry the
    /// semantics IceCrow models, so the value is kept but graded Partial.
    /// </summary>
    private static EvidenceCertainty GradePlacement(int? placement, GameMode mode)
    {
        if (placement is not int place)
        {
            return EvidenceCertainty.Unknown;
        }

        var lastPlace = mode switch
        {
            GameMode.Battlegrounds => MaximumSoloPlacement,
            GameMode.BattlegroundsDuo => MaximumDuosPlacement,
            _ => int.MaxValue,
        };
        return place <= lastPlace ? EvidenceCertainty.Exact : EvidenceCertainty.Partial;
    }

    /// <summary>
    /// The warband that entered the combat of the final round is the final
    /// board. A board frozen in an earlier round may have been reshaped in
    /// the shop before the match ended, so it is only a partial answer.
    /// </summary>
    private static EvidenceCertainty GradeFinalBoard(BoardSnapshot? board, int finalTurn) =>
        board switch
        {
            null => EvidenceCertainty.Unknown,
            _ when board.Turn == finalTurn => EvidenceCertainty.Exact,
            _ => EvidenceCertainty.Partial,
        };
}
