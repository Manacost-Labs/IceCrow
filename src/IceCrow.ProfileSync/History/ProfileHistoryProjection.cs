using System.Collections.Immutable;
using System.Text.Json;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.History;

/// <summary>Defensively maps durable profile envelopes to a UI-neutral history read model.</summary>
public static class ProfileHistoryProjection
{
    public static ProfileHistorySnapshot Create(
        IReadOnlyList<ProfileEvent> events,
        bool recoveredTruncatedTail = false)
    {
        ArgumentNullException.ThrowIfNull(events);
        var matches = ImmutableArray.CreateBuilder<HistoryMatch>();
        var skipped = 0;
        foreach (var profileEvent in events)
        {
            if (TryCreateMatch(profileEvent) is { } match)
            {
                matches.Add(match);
            }
            else
            {
                skipped++;
            }
        }

        var ordered = matches
            .OrderByDescending(static match => match.EndedAt)
            .ThenByDescending(static match => match.EventId)
            .ToImmutableArray();
        return new ProfileHistorySnapshot(
            ordered,
            CreateDecks(ordered),
            events.Count,
            skipped,
            recoveredTruncatedTail);
    }

    public static bool IsHistoricalType(string type) =>
        ProfileEventType.IsKnown(type) && !ProfileEventType.IsLatestOnly(type);

    private static HistoryMatch? TryCreateMatch(ProfileEvent profileEvent)
    {
        try
        {
            return profileEvent.Type switch
            {
                ProfileEventType.ConstructedMatch => FromConstructed(profileEvent),
                ProfileEventType.ArenaMatch => FromArena(profileEvent),
                ProfileEventType.BattlegroundsMatch => FromBattlegrounds(profileEvent),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HistoryMatch? FromConstructed(ProfileEvent profileEvent)
    {
        var record = profileEvent.Payload.Deserialize<ConstructedMatchRecord>(ProfileJson.Options);
        if (record is null || !ValidMatch(record.MatchId, record.StartedAt, record.EndedAt))
        {
            return null;
        }

        var mode = record.Format switch
        {
            "standard" => HistoryGameMode.Standard,
            "wild" => HistoryGameMode.Wild,
            _ => (HistoryGameMode?)null,
        };
        if (mode is null)
        {
            return null;
        }

        return new HistoryMatch(
            profileEvent.EventId,
            record.MatchId,
            mode.Value,
            record.Result,
            record.ResultConfidence,
            record.StartedAt,
            record.EndedAt,
            record.DurationSeconds,
            record.Turns,
            BoundedCardId(record.PlayerHeroCardId),
            BoundedCardId(record.OpponentHeroCardId),
            null,
            Certainty.Unknown,
            BoundedDeckCode(record.PlayerDeck?.DeckCode),
            BoundedHash(record.PlayerDeck?.DeckHash),
            record.PlayerDeck?.Confidence ?? Certainty.Unknown);
    }

    private static HistoryMatch? FromArena(ProfileEvent profileEvent)
    {
        var record = profileEvent.Payload.Deserialize<ArenaMatchRecord>(ProfileJson.Options);
        if (record is null || !ValidMatch(record.MatchId, record.StartedAt, record.EndedAt))
        {
            return null;
        }

        return new HistoryMatch(
            profileEvent.EventId,
            record.MatchId,
            HistoryGameMode.Arena,
            record.Result,
            record.ResultConfidence,
            record.StartedAt,
            record.EndedAt,
            record.DurationSeconds,
            record.Turns,
            BoundedCardId(record.PlayerHeroCardId),
            BoundedCardId(record.OpponentHeroCardId),
            null,
            Certainty.Unknown,
            null,
            null,
            Certainty.Unknown);
    }

    private static HistoryMatch? FromBattlegrounds(ProfileEvent profileEvent)
    {
        var record = profileEvent.Payload.Deserialize<BattlegroundsMatchRecord>(ProfileJson.Options);
        if (record is null || !ValidMatch(record.MatchId, record.StartedAt, record.EndedAt))
        {
            return null;
        }

        return new HistoryMatch(
            profileEvent.EventId,
            record.MatchId,
            HistoryGameMode.Battlegrounds,
            MatchResult.Unknown,
            Certainty.Unknown,
            record.StartedAt,
            record.EndedAt,
            record.DurationSeconds,
            record.FinalTurn,
            BoundedCardId(record.HeroCardId),
            null,
            record.Placement is >= 1 and <= 8 ? record.Placement : null,
            record.PlacementConfidence,
            null,
            null,
            Certainty.Unknown);
    }

    private static ImmutableArray<HistoryDeck> CreateDecks(ImmutableArray<HistoryMatch> matches) =>
        matches
            .Where(static match =>
                match.DeckConfidence != Certainty.Unknown &&
                (match.DeckCode is not null || match.DeckHash is not null))
            .GroupBy(static match => new DeckKey(match.Mode, match.DeckCode, match.DeckHash))
            .Select(static group => new HistoryDeck(
                group.Key.Mode,
                group.Key.DeckCode,
                group.Key.DeckHash,
                group.Min(static match => match.DeckConfidence),
                group.Count(),
                group.Count(static match => match.Result == MatchResult.Won),
                group.Count(static match => match.Result == MatchResult.Lost),
                group.Count(static match => match.Result == MatchResult.Tied),
                group.Max(static match => match.EndedAt)))
            .OrderByDescending(static deck => deck.LastPlayedAt)
            .ToImmutableArray();

    private static bool ValidMatch(Guid matchId, DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        matchId != Guid.Empty && endedAt >= startedAt;

    private static string? BoundedCardId(string? value) =>
        value is { Length: > 0 and <= ProfileRecordLimits.MaximumCardIdLength } ? value : null;

    private static string? BoundedDeckCode(string? value) =>
        value is { Length: > 0 and <= 4096 } ? value : null;

    private static string? BoundedHash(string? value) =>
        value is { Length: > 0 and <= 128 } ? value : null;

    private sealed record DeckKey(HistoryGameMode Mode, string? DeckCode, string? DeckHash);
}
