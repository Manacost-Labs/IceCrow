using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.ClientState;

public sealed class ClientChoiceState : IEquatable<ClientChoiceState>
{
    public const int MaximumChoices = 32;
    public const int MaximumCardIdLength = 128;

    private readonly string[] _cardIds;
    private readonly ReadOnlyCollection<string> _readOnlyCardIds;

    public ClientChoiceState(bool isVisible, IEnumerable<string> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        _cardIds = cardIds.Take(MaximumChoices + 1).ToArray();
        if (_cardIds.Length > MaximumChoices)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardIds),
                $"Choice state cannot contain more than {MaximumChoices} card IDs.");
        }

        foreach (var cardId in _cardIds)
        {
            if (string.IsNullOrWhiteSpace(cardId) || cardId.Length > MaximumCardIdLength)
            {
                throw new ArgumentException(
                    $"Card IDs must contain 1 to {MaximumCardIdLength} non-whitespace characters.",
                    nameof(cardIds));
            }
        }

        IsVisible = isVisible;
        _readOnlyCardIds = Array.AsReadOnly(_cardIds);
    }

    public bool IsVisible { get; }

    public IReadOnlyList<string> CardIds => _readOnlyCardIds;

    public bool Equals(ClientChoiceState? other) =>
        other is not null &&
        IsVisible == other.IsVisible &&
        _cardIds.SequenceEqual(other._cardIds, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ClientChoiceState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsVisible);
        foreach (var cardId in _cardIds)
        {
            hash.Add(cardId, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
