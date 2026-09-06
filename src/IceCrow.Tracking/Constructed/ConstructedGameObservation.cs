using IceCrow.Hearthstone.Entities;

namespace IceCrow.Tracking.Constructed;

/// <summary>
/// The player-relative facts of one open game: who the local player is, the
/// heroes, the terminal result, the mulligan, and the opponent cards that
/// gained a card id. Everything stays Unknown until the local controller is
/// identified, and hidden opponent card ids are never recorded.
/// </summary>
internal sealed class ConstructedGameObservation(
    ConstructedMatchLimits limits,
    ConstructedEntityTable entities)
{
    private readonly ConstructedMulliganCapture _mulligan = new(limits.MaximumMulliganCards);
    private readonly Dictionary<int, int> _playerEntityIds = [];
    private readonly Dictionary<int, string> _heroCardIds = [];
    private readonly List<string> _observedOpponentCards = [];
    private int? _localPlayerId;
    private ConstructedMatchResult _result;
    private EvidenceCertainty _resultCertainty;
    private bool _localResultObserved;

    public DateTimeOffset? EndedAt { get; private set; }

    public bool HasResult => _resultCertainty != EvidenceCertainty.Unknown;

    public void Clear()
    {
        _mulligan.Reset();
        _playerEntityIds.Clear();
        _heroCardIds.Clear();
        _observedOpponentCards.Clear();
        _localPlayerId = null;
        _result = ConstructedMatchResult.Unknown;
        _resultCertainty = EvidenceCertainty.Unknown;
        _localResultObserved = false;
        EndedAt = null;
    }

    public void DeclarePlayer(ConstructedEntity entity, int playerId)
    {
        if (playerId <= 0)
        {
            return;
        }

        entity.PlayerId = playerId;
        if (_playerEntityIds.ContainsKey(playerId) ||
            _playerEntityIds.Count < ConstructedMatchLimits.MaximumPlayers)
        {
            _playerEntityIds[playerId] = entity.Id;
        }
    }

    /// <summary>
    /// Creation tags decide identity: the first known card created in a hand
    /// or deck reveals the local controller (the client only knows its own
    /// card ids), and the first hero created in play per controller is that
    /// controller's hero. Any tag can complete an opponent card observation.
    /// </summary>
    public void Evaluate(ConstructedEntity entity, bool isCreationTag)
    {
        if (entity.CardId is not { } cardId || entity.Controller <= 0)
        {
            return;
        }

        if (isCreationTag)
        {
            if (_localPlayerId is null && entity.Zone is (int)Zone.Hand or (int)Zone.Deck)
            {
                _localPlayerId = entity.Controller;
            }

            if (entity.CardType == (int)CardType.Hero &&
                entity.Zone == (int)Zone.Play &&
                _heroCardIds.Count < ConstructedMatchLimits.MaximumPlayers)
            {
                _ = _heroCardIds.TryAdd(entity.Controller, cardId);
            }
        }

        RecordOpponentCard(entity, cardId);
    }

    public void ObservePlayState(ConstructedEntity entity, int value, DateTimeOffset timestamp)
    {
        if (_localPlayerId is not int local || entity.PlayerId <= 0)
        {
            return;
        }

        var isLocal = entity.PlayerId == local;
        ConstructedMatchResult? observed = (GamePlayState)value switch
        {
            GamePlayState.Won => ConstructedMatchResult.Won,
            GamePlayState.Lost => ConstructedMatchResult.Lost,
            GamePlayState.Tied => ConstructedMatchResult.Tied,
            GamePlayState.Conceded when isLocal => ConstructedMatchResult.Lost,
            _ => null,
        };
        if (observed is not { } result)
        {
            return;
        }

        if (isLocal)
        {
            _result = result;
            _localResultObserved = true;
        }
        else if (entity.PlayerId == OpponentPlayerId && !_localResultObserved)
        {
            _result = Mirror(result);
        }
        else
        {
            return;
        }

        _resultCertainty = EvidenceCertainty.Exact;
        EndedAt ??= timestamp;
    }

    public void ObserveMulliganState(ConstructedEntity entity, int value)
    {
        if (_localPlayerId is not int local || entity.PlayerId <= 0)
        {
            return;
        }

        if (entity.PlayerId == local)
        {
            _mulligan.ObserveLocalState(value, entities, local);
        }
        else if (entity.PlayerId == OpponentPlayerId)
        {
            _mulligan.ObserveOpponentState(value, entities, entity.PlayerId);
        }
    }

    public ConstructedMatchSummary CreateSummary(
        GameMetadataState metadata,
        DateTimeOffset startedAt,
        DateTimeOffset closedAt,
        int turns) => new(
            metadata.Mode,
            metadata.Format,
            _result,
            _resultCertainty,
            startedAt,
            EndedAt ?? closedAt,
            turns,
            _localPlayerId,
            HeroCardId(_localPlayerId),
            HeroCardId(OpponentPlayerId),
            _mulligan.Local,
            _mulligan.OpponentReplacedCount,
            _observedOpponentCards.ToArray(),
            metadata.BuildNumber,
            metadata.ScenarioId);

    private int? OpponentPlayerId
    {
        get
        {
            if (_localPlayerId is not int local || _playerEntityIds.Count != 2)
            {
                return null;
            }

            foreach (var playerId in _playerEntityIds.Keys)
            {
                if (playerId != local)
                {
                    return playerId;
                }
            }

            return null;
        }
    }

    private void RecordOpponentCard(ConstructedEntity entity, string cardId)
    {
        if (entity.OpponentCardRecorded ||
            OpponentPlayerId is not int opponent ||
            entity.OriginalController != opponent ||
            !IsObservableCardType(entity.CardType))
        {
            return;
        }

        entity.OpponentCardRecorded = true;
        if (_observedOpponentCards.Count < limits.MaximumObservedOpponentCards &&
            !_observedOpponentCards.Contains(cardId, StringComparer.Ordinal))
        {
            _observedOpponentCards.Add(cardId);
        }
    }

    private string? HeroCardId(int? controller) =>
        controller is int id && _heroCardIds.TryGetValue(id, out var cardId) ? cardId : null;

    // Hero, hero power, player, game and enchantment entities carry card ids
    // without being cards the opponent played or revealed from their deck.
    private static bool IsObservableCardType(int cardType) => cardType is not (
        (int)CardType.Invalid or
        (int)CardType.Game or
        (int)CardType.Player or
        (int)CardType.Hero or
        (int)CardType.HeroPower or
        (int)CardType.Enchantment);

    private static ConstructedMatchResult Mirror(ConstructedMatchResult result) => result switch
    {
        ConstructedMatchResult.Won => ConstructedMatchResult.Lost,
        ConstructedMatchResult.Lost => ConstructedMatchResult.Won,
        _ => result,
    };
}
