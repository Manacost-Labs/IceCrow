using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// The full owned collection as the client currently reports it. This is a
/// large payload: consumers must never log it and must read it on explicit
/// triggers only, never on a polling loop.
/// </summary>
public sealed class CollectionSnapshot : IEquatable<CollectionSnapshot>
{
    public const int MaximumCards = 20_000;

    private readonly CollectionCard[] _cards;
    private readonly ReadOnlyCollection<CollectionCard> _readOnlyCards;

    public CollectionSnapshot(DateTimeOffset observedAt, IEnumerable<CollectionCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        _cards = cards.Take(MaximumCards + 1).ToArray();
        if (_cards.Length > MaximumCards)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cards),
                $"A collection snapshot cannot contain more than {MaximumCards} cards.");
        }

        if (Array.Exists(_cards, static card => card is null))
        {
            throw new ArgumentException("Collection entries cannot be null.", nameof(cards));
        }

        ObservedAt = observedAt;
        _readOnlyCards = Array.AsReadOnly(_cards);
    }

    public DateTimeOffset ObservedAt { get; }

    public IReadOnlyList<CollectionCard> Cards => _readOnlyCards;

    public bool Equals(CollectionSnapshot? other) =>
        other is not null &&
        ObservedAt == other.ObservedAt &&
        _cards.AsSpan().SequenceEqual(other._cards);

    public override bool Equals(object? obj) => Equals(obj as CollectionSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ObservedAt);
        hash.Add(_cards.Length);
        foreach (var card in _cards)
        {
            hash.Add(card);
        }

        return hash.ToHashCode();
    }
}
