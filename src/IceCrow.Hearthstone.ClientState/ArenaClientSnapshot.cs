using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// Current Arena client state: the draft in progress or the run being played.
/// The client does not always offer exactly three cards, so choices are a
/// bounded ordered list rather than a fixed triple. <see cref="RunKey"/> is an
/// opaque client run identifier when the source exposes one and is never parsed.
/// </summary>
public sealed class ArenaClientSnapshot : IEquatable<ArenaClientSnapshot>
{
    public const int MaximumRunKeyLength = 128;
    public const int MaximumHeroCardIdLength = 64;
    public const int MaximumDeckCodeLength = 512;
    public const int MaximumChoices = 8;
    public const int MaximumDeckCards = 40;
    public const int MaximumScore = 20;
    public const int MaximumRating = 100_000;

    private readonly string[] _currentChoiceCardIds;
    private readonly string[] _deckCardIds;
    private readonly ReadOnlyCollection<string> _readOnlyChoices;
    private readonly ReadOnlyCollection<string> _readOnlyDeck;

    public ArenaClientSnapshot(
        DateTimeOffset observedAt,
        string? runKey,
        bool isDrafting,
        int wins,
        int losses,
        bool? isRunComplete,
        string? heroCardId,
        IEnumerable<string> currentChoiceCardIds,
        IEnumerable<string> deckCardIds,
        string? deckCode,
        int? rating)
    {
        ObservedAt = observedAt;
        RunKey = ClientCardIds.ValidateOptionalToken(runKey, MaximumRunKeyLength, nameof(runKey));
        IsDrafting = isDrafting;
        Wins = ValidateScore(wins, nameof(wins));
        Losses = ValidateScore(losses, nameof(losses));
        IsRunComplete = isRunComplete;
        HeroCardId = ClientCardIds.ValidateOptionalToken(heroCardId, MaximumHeroCardIdLength, nameof(heroCardId));
        _currentChoiceCardIds = ClientCardIds.CopyBounded(currentChoiceCardIds, MaximumChoices, nameof(currentChoiceCardIds));
        _deckCardIds = ClientCardIds.CopyBounded(deckCardIds, MaximumDeckCards, nameof(deckCardIds));
        DeckCode = ClientCardIds.ValidateOptionalToken(deckCode, MaximumDeckCodeLength, nameof(deckCode));
        Rating = ClientCardIds.ValidateOptionalRange(rating, 0, MaximumRating, nameof(rating));
        _readOnlyChoices = ClientCardIds.AsReadOnly(_currentChoiceCardIds);
        _readOnlyDeck = ClientCardIds.AsReadOnly(_deckCardIds);
    }

    public DateTimeOffset ObservedAt { get; }

    public string? RunKey { get; }

    public bool IsDrafting { get; }

    public int Wins { get; }

    public int Losses { get; }

    /// <summary>Null when the client does not expose run completion; never inferred from the score.</summary>
    public bool? IsRunComplete { get; }

    public string? HeroCardId { get; }

    public IReadOnlyList<string> CurrentChoiceCardIds => _readOnlyChoices;

    public IReadOnlyList<string> DeckCardIds => _readOnlyDeck;

    public string? DeckCode { get; }

    public int? Rating { get; }

    public bool Equals(ArenaClientSnapshot? other) =>
        other is not null &&
        ObservedAt == other.ObservedAt &&
        string.Equals(RunKey, other.RunKey, StringComparison.Ordinal) &&
        IsDrafting == other.IsDrafting &&
        Wins == other.Wins &&
        Losses == other.Losses &&
        IsRunComplete == other.IsRunComplete &&
        string.Equals(HeroCardId, other.HeroCardId, StringComparison.Ordinal) &&
        ClientCardIds.SequenceEquals(_currentChoiceCardIds, other._currentChoiceCardIds) &&
        ClientCardIds.SequenceEquals(_deckCardIds, other._deckCardIds) &&
        string.Equals(DeckCode, other.DeckCode, StringComparison.Ordinal) &&
        Rating == other.Rating;

    public override bool Equals(object? obj) => Equals(obj as ArenaClientSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ObservedAt);
        hash.Add(RunKey, StringComparer.Ordinal);
        hash.Add(IsDrafting);
        hash.Add(Wins);
        hash.Add(Losses);
        hash.Add(IsRunComplete);
        hash.Add(HeroCardId, StringComparer.Ordinal);
        ClientCardIds.AddToHash(ref hash, _currentChoiceCardIds);
        ClientCardIds.AddToHash(ref hash, _deckCardIds);
        hash.Add(DeckCode, StringComparer.Ordinal);
        hash.Add(Rating);
        return hash.ToHashCode();
    }

    private static int ValidateScore(int value, string parameterName)
    {
        if (value is < 0 or > MaximumScore)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Arena scores must be between 0 and {MaximumScore}.");
        }

        return value;
    }
}
