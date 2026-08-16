using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Live;

/// <summary>
/// Sole lifecycle authority for Battlegrounds match detection. A Battlegrounds
/// tag is only <em>candidate</em> evidence: the client's catch-up dump after a
/// live <c>log.config</c> change replays the current UI context as a burst of
/// tags that all share one log timestamp and never progress. A match is
/// <em>confirmed</em> only when a later-timestamped event follows the armed
/// evidence — real matches advance log time immediately, a catch-up snapshot
/// never does.
/// </summary>
public sealed class BattlegroundsLifecycleDetector
{
    public const int DefaultMaximumPlayerIdentities = 256;
    public const int DefaultPlayerIdentityWarningThreshold = 192;

    private readonly Dictionary<int, int> _playerIdsByEntity = [];
    private readonly Queue<int> _playerIdentityOrder = [];
    private readonly int _maximumPlayerIdentities;
    private readonly int _playerIdentityWarningThreshold;
    private bool _battlegroundsDetected;
    private int? _localPlayerEntityId;
    private DateTimeOffset? _candidateStartedAt;
    private BattlegroundsLifecycleEvidence _pendingEvidence;
    private DateTimeOffset? _pendingEvidenceAt;

    public BattlegroundsLifecycleDetector(
        int maximumPlayerIdentities = DefaultMaximumPlayerIdentities,
        int playerIdentityWarningThreshold = DefaultPlayerIdentityWarningThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPlayerIdentities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerIdentityWarningThreshold);
        if (playerIdentityWarningThreshold > maximumPlayerIdentities)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerIdentityWarningThreshold),
                playerIdentityWarningThreshold,
                "The warning threshold cannot exceed the hard identity limit.");
        }

        _maximumPlayerIdentities = maximumPlayerIdentities;
        _playerIdentityWarningThreshold = playerIdentityWarningThreshold;
    }

    public int PlayerIdentityCount => _playerIdsByEntity.Count;

    public int PlayerIdentityWarningThreshold => _playerIdentityWarningThreshold;

    public long PlayerIdentityEvictions { get; private set; }

    public BattlegroundsLifecycleObservation Observe(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (gameEvent is GameCreated created)
        {
            ResetCandidate(created.Timestamp);
            return new BattlegroundsLifecycleObservation(
                BattlegroundsLifecycleAction.GameBoundary,
                created.Timestamp,
                LocalPlayerId: null,
                BattlegroundsLifecycleEvidence.CreateGame);
        }

        _candidateStartedAt ??= gameEvent.Timestamp;
        if (gameEvent is PlayerEntityDeclared player)
        {
            RememberPlayerIdentity(player.EntityId, player.PlayerId);
        }

        if (gameEvent is not RawTagChanged tagChanged)
        {
            return ConfirmPendingOrNone(gameEvent.Timestamp);
        }

        ObservePlayerIdentity(tagChanged);
        if (IsCompleteGameState(tagChanged))
        {
            // A complete-state tag inside an unconfirmed burst is snapshot
            // residue of an already-finished game, never live match evidence.
            ClearPendingEvidence();
            return new BattlegroundsLifecycleObservation(
                BattlegroundsLifecycleAction.EndMatch,
                _candidateStartedAt ?? gameEvent.Timestamp,
                GetLocalPlayerId(),
                BattlegroundsLifecycleEvidence.StateComplete);
        }

        var evidence = GetBattlegroundsEvidence(tagChanged);
        if (!_battlegroundsDetected &&
            _pendingEvidence == BattlegroundsLifecycleEvidence.None &&
            evidence != BattlegroundsLifecycleEvidence.None)
        {
            _pendingEvidence = evidence;
            _pendingEvidenceAt = gameEvent.Timestamp;
            return None(gameEvent.Timestamp);
        }

        return ConfirmPendingOrNone(gameEvent.Timestamp);
    }

    public void Reset()
    {
        _playerIdsByEntity.Clear();
        _playerIdentityOrder.Clear();
        _battlegroundsDetected = false;
        _localPlayerEntityId = null;
        _candidateStartedAt = null;
        ClearPendingEvidence();
    }

    private BattlegroundsLifecycleObservation ConfirmPendingOrNone(DateTimeOffset timestamp)
    {
        if (_battlegroundsDetected ||
            _pendingEvidence == BattlegroundsLifecycleEvidence.None ||
            _pendingEvidenceAt is not DateTimeOffset armedAt ||
            timestamp <= armedAt)
        {
            return None(timestamp);
        }

        var evidence = _pendingEvidence;
        ClearPendingEvidence();
        _battlegroundsDetected = true;
        return new BattlegroundsLifecycleObservation(
            BattlegroundsLifecycleAction.StartBattlegrounds,
            _candidateStartedAt ?? timestamp,
            GetLocalPlayerId(),
            evidence);
    }

    private void ClearPendingEvidence()
    {
        _pendingEvidence = BattlegroundsLifecycleEvidence.None;
        _pendingEvidenceAt = null;
    }

    private void ResetCandidate(DateTimeOffset timestamp)
    {
        Reset();
        _candidateStartedAt = timestamp;
    }

    private void ObservePlayerIdentity(RawTagChanged tagChanged)
    {
        if (tagChanged.EntityId is not int entityId)
        {
            return;
        }

        if (string.Equals(tagChanged.Tag, "PLAYER_ID", StringComparison.Ordinal) &&
            int.TryParse(
                tagChanged.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var playerId) &&
            playerId > 0)
        {
            RememberPlayerIdentity(entityId, playerId);
        }

        if (string.Equals(
                tagChanged.Tag,
                "NEXT_OPPONENT_PLAYER_ID",
                StringComparison.Ordinal))
        {
            // HDT TagChangeActions.OnNextOpponentPlayerId at revision
            // d73b3a8220bbc88e836af8cd67a15c44a1fb7021, inspected 2026-08-14,
            // accepts this tag only when it belongs to game.PlayerEntity.
            _localPlayerEntityId = entityId;
        }
    }

    private int? GetLocalPlayerId() =>
        _localPlayerEntityId is int entityId &&
        _playerIdsByEntity.TryGetValue(entityId, out var playerId)
            ? playerId
            : null;

    private void RememberPlayerIdentity(int entityId, int playerId)
    {
        if (_playerIdsByEntity.ContainsKey(entityId))
        {
            _playerIdsByEntity[entityId] = playerId;
            return;
        }

        while (_playerIdsByEntity.Count >= _maximumPlayerIdentities &&
               _playerIdentityOrder.TryDequeue(out var oldestEntityId))
        {
            if (_playerIdsByEntity.Remove(oldestEntityId))
            {
                if (PlayerIdentityEvictions < long.MaxValue)
                {
                    PlayerIdentityEvictions++;
                }

                break;
            }
        }

        _playerIdsByEntity.Add(entityId, playerId);
        _playerIdentityOrder.Enqueue(entityId);
    }

    private static BattlegroundsLifecycleEvidence GetBattlegroundsEvidence(
        RawTagChanged tagChanged) => tagChanged.Tag switch
        {
            // These named tags are handled as Battlegrounds state in HDT
            // TagChangeActions.cs at revision d73b3a8220bbc88e836af8cd67a15c44a1fb7021.
            // HERO_ENTITY is intentionally excluded because HDT also uses it for
            // traditional Hearthstone games.
            "PLAYER_TECH_LEVEL" => BattlegroundsLifecycleEvidence.PlayerTechLevel,
            "PLAYER_TRIPLES" => BattlegroundsLifecycleEvidence.PlayerTriples,
            "NEXT_OPPONENT_PLAYER_ID" => BattlegroundsLifecycleEvidence.NextOpponentPlayerId,
            _ => BattlegroundsLifecycleEvidence.None,
        };

    private static bool IsCompleteGameState(RawTagChanged tagChanged) =>
        string.Equals(tagChanged.Tag, "STATE", StringComparison.Ordinal) &&
        string.Equals(tagChanged.Value, "COMPLETE", StringComparison.Ordinal);

    private BattlegroundsLifecycleObservation None(DateTimeOffset timestamp) => new(
        BattlegroundsLifecycleAction.None,
        _candidateStartedAt ?? timestamp,
        GetLocalPlayerId(),
        BattlegroundsLifecycleEvidence.None);
}

public enum BattlegroundsLifecycleAction
{
    None,
    GameBoundary,
    StartBattlegrounds,
    EndMatch,
}

public enum BattlegroundsLifecycleEvidence
{
    None,
    CreateGame,
    PlayerTechLevel,
    PlayerTriples,
    NextOpponentPlayerId,
    StateComplete,
}

public sealed record BattlegroundsLifecycleObservation(
    BattlegroundsLifecycleAction Action,
    DateTimeOffset MatchStartedAt,
    int? LocalPlayerId,
    BattlegroundsLifecycleEvidence Evidence);
