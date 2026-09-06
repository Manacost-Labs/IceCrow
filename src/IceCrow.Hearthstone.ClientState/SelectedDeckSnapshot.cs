using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// The deck the client currently has selected. The deck code is passed through
/// from the client; IceCrow never builds one from the card list.
/// </summary>
public sealed class SelectedDeckSnapshot : IEquatable<SelectedDeckSnapshot>
{
    public const int MaximumDeckCodeLength = 512;
    public const int MaximumTokenLength = 64;
    public const int MaximumCards = 60;

    private readonly string[] _cardIds;
    private readonly ReadOnlyCollection<string> _readOnlyCardIds;

    public SelectedDeckSnapshot(
        DateTimeOffset observedAt,
        string? deckCode,
        string? heroCardId,
        string? formatToken,
        IEnumerable<string> cardIds)
    {
        ObservedAt = observedAt;
        DeckCode = ClientCardIds.ValidateOptionalToken(deckCode, MaximumDeckCodeLength, nameof(deckCode));
        HeroCardId = ClientCardIds.ValidateOptionalToken(heroCardId, MaximumTokenLength, nameof(heroCardId));
        FormatToken = ClientCardIds.ValidateOptionalToken(formatToken, MaximumTokenLength, nameof(formatToken));
        _cardIds = ClientCardIds.CopyBounded(cardIds, MaximumCards, nameof(cardIds));
        _readOnlyCardIds = ClientCardIds.AsReadOnly(_cardIds);
    }

    public DateTimeOffset ObservedAt { get; }

    public string? DeckCode { get; }

    public string? HeroCardId { get; }

    /// <summary>Opaque client format token (for example a Standard/Wild marker); never interpreted here.</summary>
    public string? FormatToken { get; }

    public IReadOnlyList<string> CardIds => _readOnlyCardIds;

    public bool Equals(SelectedDeckSnapshot? other) =>
        other is not null &&
        ObservedAt == other.ObservedAt &&
        string.Equals(DeckCode, other.DeckCode, StringComparison.Ordinal) &&
        string.Equals(HeroCardId, other.HeroCardId, StringComparison.Ordinal) &&
        string.Equals(FormatToken, other.FormatToken, StringComparison.Ordinal) &&
        ClientCardIds.SequenceEquals(_cardIds, other._cardIds);

    public override bool Equals(object? obj) => Equals(obj as SelectedDeckSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ObservedAt);
        hash.Add(DeckCode, StringComparer.Ordinal);
        hash.Add(HeroCardId, StringComparer.Ordinal);
        hash.Add(FormatToken, StringComparer.Ordinal);
        ClientCardIds.AddToHash(ref hash, _cardIds);
        return hash.ToHashCode();
    }
}
