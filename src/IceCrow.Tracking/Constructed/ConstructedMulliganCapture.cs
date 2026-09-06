using IceCrow.Hearthstone.Entities;

namespace IceCrow.Tracking.Constructed;

/// <summary>
/// Captures the local mulligan from visible zone transitions and counts the
/// opponent's replaced cards without ever recording a hidden card id. Each
/// player's INPUT and DONE are latched once per game because the real client
/// prints every power line twice (GameState first, PowerTaskList later) and a
/// replayed INPUT must not re-capture the post-mulligan hand.
/// </summary>
internal sealed class ConstructedMulliganCapture(int maximumCards)
{
    private enum Phase
    {
        None,
        Input,
        Done,
    }

    private readonly List<(int EntityId, string CardId)> _localInitial = [];
    private readonly List<int> _opponentInitial = [];
    private Phase _localPhase;
    private Phase _opponentPhase;

    public ConstructedMulligan Local { get; private set; } = ConstructedMulligan.Unknown;

    public int? OpponentReplacedCount { get; private set; }

    public void Reset()
    {
        _localInitial.Clear();
        _opponentInitial.Clear();
        _localPhase = Phase.None;
        _opponentPhase = Phase.None;
        Local = ConstructedMulligan.Unknown;
        OpponentReplacedCount = null;
    }

    public void ObserveLocalState(int state, ConstructedEntityTable entities, int localPlayerId)
    {
        if (state == (int)MulliganState.Input && _localPhase == Phase.None)
        {
            _localPhase = Phase.Input;
            foreach (var card in HandOf(entities, localPlayerId))
            {
                if (card.CardId is { } cardId)
                {
                    _localInitial.Add((card.Id, cardId));
                }
            }
        }
        else if (state == (int)MulliganState.Done && _localPhase == Phase.Input)
        {
            _localPhase = Phase.Done;
            Local = CreateLocalMulligan(HandOf(entities, localPlayerId));
        }
    }

    public void ObserveOpponentState(int state, ConstructedEntityTable entities, int opponentPlayerId)
    {
        if (state == (int)MulliganState.Input && _opponentPhase == Phase.None)
        {
            _opponentPhase = Phase.Input;
            foreach (var card in HandOf(entities, opponentPlayerId))
            {
                _opponentInitial.Add(card.Id);
            }
        }
        else if (state == (int)MulliganState.Done && _opponentPhase == Phase.Input)
        {
            _opponentPhase = Phase.Done;
            var replaced = 0;
            foreach (var entityId in _opponentInitial)
            {
                if (!entities.TryGet(entityId, out var entity) ||
                    entity.Zone != (int)Zone.Hand ||
                    entity.Controller != opponentPlayerId)
                {
                    replaced++;
                }
            }

            OpponentReplacedCount = replaced;
        }
    }

    private ConstructedMulligan CreateLocalMulligan(List<ConstructedEntity> handAtDone)
    {
        var initial = new List<string>(_localInitial.Count);
        var kept = new List<string>();
        var replaced = new List<string>();
        foreach (var (entityId, cardId) in _localInitial)
        {
            initial.Add(cardId);
            (handAtDone.Exists(card => card.Id == entityId) ? kept : replaced).Add(cardId);
        }

        var after = new List<string>(handAtDone.Count);
        foreach (var card in handAtDone)
        {
            if (card.CardId is { } cardId)
            {
                after.Add(cardId);
            }
        }

        return new ConstructedMulligan(initial, kept, replaced, after, EvidenceCertainty.Exact);
    }

    private List<ConstructedEntity> HandOf(ConstructedEntityTable entities, int controller)
    {
        var hand = new List<ConstructedEntity>();
        foreach (var entity in entities.Entities)
        {
            if (entity.Controller == controller && entity.Zone == (int)Zone.Hand)
            {
                hand.Add(entity);
            }
        }

        hand.Sort(static (left, right) =>
        {
            var byPosition = left.ZonePosition.CompareTo(right.ZonePosition);
            return byPosition != 0 ? byPosition : left.Id.CompareTo(right.Id);
        });
        if (hand.Count > maximumCards)
        {
            hand.RemoveRange(maximumCards, hand.Count - maximumCards);
        }

        return hand;
    }
}
