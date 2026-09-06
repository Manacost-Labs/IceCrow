using System.Threading.Channels;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.Live;

/// <summary>
/// Single-consumer router over one <see cref="PowerLineParser"/>, the
/// Battlegrounds <see cref="LiveTrackingCoordinator"/> and the lightweight
/// <see cref="ConstructedMatchTracker"/>. Each line is parsed exactly once.
/// Routing per game: <c>CREATE_GAME</c> and every metadata line always reach
/// both trackers (the Battlegrounds coordinator needs the boundary to end a
/// stale match); while the mode is unknown both receive everything; once the
/// metadata block names Battlegrounds only the Battlegrounds coordinator is
/// fed, Ranked and Arena feed only the Constructed tracker, and any other
/// mode feeds neither for the rest of that game. Lines are still counted.
/// </summary>
public sealed class GameSessionCoordinator
{
    private readonly PowerLineParser _parser;
    private readonly LiveTrackingCoordinator _battlegrounds;
    private readonly ConstructedMatchTracker _constructed;
    private GameSessionRoute _route = GameSessionRoute.Both;
    private long _rawLinesReceived;
    private long _parsedEvents;
    private long _ignored;
    private long _unknown;
    private long _malformed;
    private long _battlegroundsLinesRouted;
    private long _constructedEventsRouted;

    public GameSessionCoordinator(
        PowerLineParser? parser = null,
        TrackingSession? tracking = null,
        int pendingEventCapacity = LiveTrackingCoordinator.DefaultPendingEventCapacity,
        IAppliedMatchEventObserver? appliedEventObserver = null,
        ConstructedMatchLimits? constructedLimits = null)
    {
        _parser = parser ?? new PowerLineParser();
        _battlegrounds = new LiveTrackingCoordinator(
            _parser,
            tracking,
            lifecycle: null,
            pendingEventCapacity,
            appliedEventObserver);
        _constructed = new ConstructedMatchTracker(constructedLimits);
    }

    public LiveTrackingCoordinator Battlegrounds => _battlegrounds;

    public ConstructedMatchTracker Constructed => _constructed;

    public GameSessionRoute Route => _route;

    public GameSessionDiagnostics Diagnostics => CreateDiagnostics();

    public GameSessionUpdate Process(RawLogLine rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        Increment(ref _rawLinesReceived);
        var parseResult = _parser.Parse(rawLine.Content, rawLine.Timestamp);
        CountParseResult(parseResult.Status);
        var gameEvent = parseResult.Status == PowerParseStatus.Parsed ? parseResult.Event : null;
        var isBoundary = gameEvent is GameCreated or GameMetadataObserved;

        LiveTrackingUpdate? battlegrounds = null;
        if (isBoundary || _route is GameSessionRoute.Both or GameSessionRoute.Battlegrounds)
        {
            battlegrounds = _battlegrounds.ProcessParsed(rawLine, parseResult);
            Increment(ref _battlegroundsLinesRouted);
        }

        ConstructedMatchSummary? completed = null;
        if (gameEvent is not null &&
            (isBoundary || _route is GameSessionRoute.Both or GameSessionRoute.Constructed))
        {
            completed = _constructed.Apply(gameEvent).CompletedMatch;
            Increment(ref _constructedEventsRouted);
        }

        if (isBoundary)
        {
            _route = RouteFor(_constructed.Metadata);
        }

        var metadata = _constructed.Metadata;
        return new GameSessionUpdate(
            rawLine,
            parseResult,
            battlegrounds,
            completed,
            metadata,
            metadata.Mode,
            battlegrounds?.StateChanged == true || completed is not null);
    }

    public async Task RunAsync(
        ChannelReader<RawLogLine> lines,
        Action<GameSessionUpdate>? onProcessed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        try
        {
            await foreach (var line in lines.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                onProcessed?.Invoke(Process(line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Reset()
    {
        _battlegrounds.Reset();
        _constructed.Reset();
        _route = GameSessionRoute.Both;
    }

    // The metadata block may name a mode IceCrow does not collect, or an
    // explicitly unknown one; both stop gameplay routing for the game.
    private static GameSessionRoute RouteFor(GameMetadataState metadata)
    {
        if (metadata.GameTypeToken is null)
        {
            return GameSessionRoute.Both;
        }

        return metadata.Mode switch
        {
            GameMode.Battlegrounds or GameMode.BattlegroundsDuo => GameSessionRoute.Battlegrounds,
            GameMode.Ranked or GameMode.Arena => GameSessionRoute.Constructed,
            _ => GameSessionRoute.None,
        };
    }

    private void CountParseResult(PowerParseStatus status)
    {
        switch (status)
        {
            case PowerParseStatus.Parsed:
                Increment(ref _parsedEvents);
                break;
            case PowerParseStatus.Ignored:
                Increment(ref _ignored);
                break;
            case PowerParseStatus.Malformed:
                Increment(ref _malformed);
                break;
            case PowerParseStatus.Unknown:
                Increment(ref _unknown);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private GameSessionDiagnostics CreateDiagnostics() => new(
        _battlegrounds.Diagnostics,
        _rawLinesReceived,
        _parsedEvents,
        _ignored,
        _unknown,
        _malformed,
        _battlegroundsLinesRouted,
        _constructedEventsRouted,
        _constructed.GamesSeen,
        _constructed.CompletedMatches,
        _constructed.IgnoredModeGames,
        _constructed.EntityCount,
        _constructed.RejectedEntities,
        _constructed.Metadata.Mode,
        _route);

    private static void Increment(ref long counter)
    {
        if (counter < long.MaxValue)
        {
            counter++;
        }
    }
}
