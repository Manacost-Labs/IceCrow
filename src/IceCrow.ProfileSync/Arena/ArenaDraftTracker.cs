using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Proves draft picks from consecutive snapshots of one run. An offer becomes
/// pending when it appears; it is resolved when the deck gains exactly one
/// card from it. Because the client may update the choice list and the deck
/// list in separate reads, a small queue of pending offers is kept, and an
/// offer that never produces a deck card (for example the hero choice) is
/// simply superseded. Any deck change that cannot be attributed to a single
/// offered card is counted as a gap and never guessed.
/// </summary>
internal sealed class ArenaDraftTracker
{
    public const int MaximumPendingOffers = 3;

    private readonly List<string[]> _pendingOffers = new(MaximumPendingOffers);
    private string[] _lastChoices = [];
    private IReadOnlyList<string> _lastDeck = [];

    public bool HasPrevious { get; private set; }

    public int LastDeckCount => _lastDeck.Count;

    public int PickCount { get; private set; }

    public int GapCount { get; private set; }

    public ArenaDraftPickRecord? Observe(Guid runId, ArenaClientSnapshot snapshot)
    {
        var pick = HasPrevious ? ClassifyDeckChange(runId, snapshot) : null;
        TrackOffer(snapshot);
        _lastDeck = snapshot.DeckCardIds;
        HasPrevious = true;
        return pick;
    }

    private ArenaDraftPickRecord? ClassifyDeckChange(Guid runId, ArenaClientSnapshot snapshot)
    {
        var change = ArenaDeckChange.Between(_lastDeck, snapshot.DeckCardIds);
        switch (change.Kind)
        {
            case ArenaDeckChangeKind.Unchanged:
                return null;
            case ArenaDeckChangeKind.SingleGain:
                return ResolveOffer(runId, change.GainedCardId!, snapshot.ObservedAt);
            default:
                GapCount++;
                _pendingOffers.Clear();
                return null;
        }
    }

    private ArenaDraftPickRecord? ResolveOffer(Guid runId, string chosenCardId, DateTimeOffset observedAt)
    {
        var index = _pendingOffers.FindIndex(offer => offer.Contains(chosenCardId, StringComparer.Ordinal));
        if (index < 0)
        {
            GapCount++;
            _pendingOffers.Clear();
            return null;
        }

        var offer = _pendingOffers[index];
        _pendingOffers.RemoveRange(0, index + 1);
        if (PickCount >= ProfileRecordLimits.MaximumArenaPicks)
        {
            GapCount++;
            return null;
        }

        var record = new ArenaDraftPickRecord(
            runId,
            PickCount,
            Array.AsReadOnly(offer),
            chosenCardId,
            observedAt,
            Certainty.Exact);
        PickCount++;
        return record;
    }

    private void TrackOffer(ArenaClientSnapshot snapshot)
    {
        var choices = snapshot.CurrentChoiceCardIds;
        if (choices.Count == 0 || choices.SequenceEqual(_lastChoices, StringComparer.Ordinal))
        {
            return;
        }

        _lastChoices = choices.ToArray();
        if (_pendingOffers.Count == MaximumPendingOffers)
        {
            _pendingOffers.RemoveAt(0);
        }

        _pendingOffers.Add(_lastChoices);
    }
}
