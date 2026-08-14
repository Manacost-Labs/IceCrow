using IceCrow.Battlegrounds.Memory;
using IceCrow.Tracking;

namespace IceCrow.Telemetry;

public static class MatchSummaryFactory
{
    public const int CurrentSchemaVersion = 1;

    public static MatchSummary? Create(
        TrackingSnapshot snapshot,
        string clientVersion,
        string? hearthstonePatch = null,
        string queueMode = "unknown")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);
        if (snapshot.SessionState != TrackingSessionState.Ended ||
            snapshot.StartedAt is not DateTimeOffset startedAt ||
            snapshot.EndedAt is not DateTimeOffset endedAt ||
            snapshot.Battlegrounds.LocalPlayerId is not int localPlayerId)
        {
            return null;
        }

        var player = snapshot.Battlegrounds.Lobby.GetPlayer(localPlayerId);
        var timeline = snapshot.LobbyTimeline.GetPlayer(localPlayerId);
        var progression = timeline?.Events
            .OfType<TavernUpgraded>()
            .Select(item => new TavernProgressionEntry(item.TavernTier, item.Turn))
            .ToArray() ?? [];

        return new MatchSummary(
            CurrentSchemaVersion,
            Guid.CreateVersion7(),
            "battlegrounds",
            queueMode,
            hearthstonePatch,
            clientVersion,
            player?.HeroCardId,
            null,
            null,
            snapshot.Battlegrounds.Turn,
            progression,
            player?.Triples ?? timeline?.Triples ?? 0,
            startedAt,
            endedAt);
    }
}
