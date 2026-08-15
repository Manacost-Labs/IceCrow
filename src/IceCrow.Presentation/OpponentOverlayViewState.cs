using System.Globalization;

namespace IceCrow.Presentation;

/// <summary>
/// Everything the overlay needs to render one opponent, in hierarchy order:
/// who it is, what changed, what needs attention, then the board.
/// </summary>
/// <remarks>
/// Value equality is intentional. The overlay compares the previous and current
/// view state and only touches WPF when a visible value actually changed.
/// </remarks>
public sealed class OpponentOverlayViewState : IEquatable<OpponentOverlayViewState>
{
    /// <summary>Turns after which recorded board information is treated as outdated.</summary>
    public const int StaleTurnThreshold = 3;

    /// <summary>Turns after which recorded board information is historical background only.</summary>
    public const int VeryStaleTurnThreshold = 6;

    /// <summary>Tavern tier at which an opponent is highlighted as dangerous.</summary>
    public const int AttentionTavernTier = 5;

    /// <summary>Triple count at which an opponent is highlighted as dangerous.</summary>
    public const int AttentionTripleCount = 2;

    private readonly string[] _progressionRows;
    private readonly MinionTileViewState[] _board;

    public OpponentOverlayViewState(
        int playerId,
        string heroName,
        string? heroCardId,
        int tavernTier,
        int health,
        int armor,
        OpponentPresence presence,
        int? lastSeenTurn,
        int? turnsAgo,
        int triples,
        IEnumerable<string> progressionRows,
        IEnumerable<MinionTileViewState> board,
        OpponentChangesViewState? changes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(heroName);
        ArgumentOutOfRangeException.ThrowIfNegative(tavernTier);
        ArgumentOutOfRangeException.ThrowIfNegative(triples);
        ArgumentNullException.ThrowIfNull(progressionRows);
        ArgumentNullException.ThrowIfNull(board);

        PlayerId = playerId;
        HeroName = heroName;
        HeroCardId = heroCardId;
        TavernTier = tavernTier;
        Health = health;
        Armor = armor;
        Presence = presence;
        LastSeenTurn = lastSeenTurn;
        TurnsAgo = turnsAgo;
        Triples = triples;
        _progressionRows = progressionRows.ToArray();
        ProgressionRows = Array.AsReadOnly(_progressionRows);
        _board = board.ToArray();
        Board = Array.AsReadOnly(_board);
        Changes = changes;
    }

    public int PlayerId { get; }

    public string HeroName { get; }

    public string? HeroCardId { get; }

    public int TavernTier { get; }

    public int Health { get; }

    public int Armor { get; }

    public OpponentPresence Presence { get; }

    public int? LastSeenTurn { get; }

    public int? TurnsAgo { get; }

    public int Triples { get; }

    public IReadOnlyList<string> ProgressionRows { get; }

    public IReadOnlyList<MinionTileViewState> Board { get; }

    /// <summary>Board movement since the previous fight; <see langword="null"/> before a second fight.</summary>
    public OpponentChangesViewState? Changes { get; }

    /// <summary>Primary tier marker, for example <c>T5</c>.</summary>
    public string TierBadge => TavernTier > 0
        ? string.Create(CultureInfo.InvariantCulture, $"T{TavernTier}")
        : "T?";

    /// <summary>Compact health summary, for example <c>28 HP</c> or <c>28 HP · 5 armor</c>.</summary>
    public string HealthLine => Health <= 0
        ? "— HP"
        : Armor > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Health} HP · {Armor} armor")
            : string.Create(CultureInfo.InvariantCulture, $"{Health} HP");

    /// <summary>Secondary line for the lobby row.</summary>
    public string LastSeenLine => Presence switch
    {
        OpponentPresence.NotFought => "Not fought yet",
        OpponentPresence.EmptyBoard when LastSeenTurn is int emptyTurn =>
            string.Create(CultureInfo.InvariantCulture, $"Board empty · turn {emptyTurn}"),
        OpponentPresence.EmptyBoard => "Board empty",
        _ when LastSeenTurn is int turn => TurnsAgo switch
        {
            0 or null => string.Create(CultureInfo.InvariantCulture, $"Turn {turn} · now"),
            1 => string.Create(CultureInfo.InvariantCulture, $"Turn {turn} · 1 turn ago"),
            int ago => string.Create(CultureInfo.InvariantCulture, $"Turn {turn} · {ago} turns ago"),
        },
        _ => "Seen",
    };

    /// <summary>Title line for the detail surface.</summary>
    public string DetailLastSeenLine => LastSeenTurn is int turn
        ? string.Create(CultureInfo.InvariantCulture, $"Last seen · Turn {turn}")
        : "Never fought";

    /// <summary>Compact age marker, for example <c>2T</c>.</summary>
    public string AgeBadge => Presence == OpponentPresence.NotFought
        ? "—"
        : TurnsAgo switch
        {
            0 or null => "NOW",
            int ago => string.Create(CultureInfo.InvariantCulture, $"{ago}T"),
        };

    /// <summary>Recorded information is old enough that it may no longer be accurate.</summary>
    public bool IsStale => TurnsAgo >= StaleTurnThreshold;

    /// <summary>Age of the recorded board; <see langword="null"/> when never fought.</summary>
    public OpponentStaleness? Staleness => Presence == OpponentPresence.NotFought
        ? null
        : TurnsAgo switch
        {
            null or 0 => OpponentStaleness.Fresh,
            < StaleTurnThreshold => OpponentStaleness.Recent,
            < VeryStaleTurnThreshold => OpponentStaleness.Stale,
            _ => OpponentStaleness.VeryStale,
        };

    /// <summary>Change summary for the lobby row, for example <c>3 changes</c>.</summary>
    public string? CompactChangeLine => Changes?.ChangeCount switch
    {
        null or 0 => null,
        1 => "1 change",
        int count => string.Create(CultureInfo.InvariantCulture, $"{count} changes"),
    };

    public string TriplesLine => Triples switch
    {
        0 => "No triples",
        1 => "1 triple",
        _ => string.Create(CultureInfo.InvariantCulture, $"{Triples} triples"),
    };

    /// <summary>
    /// Drives the IceCrow signature accent. Attention is carried by text as well
    /// as colour so the state is readable without relying on hue alone.
    /// </summary>
    public bool RequiresAttention =>
        TavernTier >= AttentionTavernTier || Triples >= AttentionTripleCount;

    /// <summary>Message shown instead of tiles when there is no recorded board.</summary>
    public string? BoardPlaceholder => Board.Count > 0
        ? null
        : Presence == OpponentPresence.NotFought
            ? "Not fought yet"
            : "Board was empty";

    public bool Equals(OpponentOverlayViewState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               PlayerId == other.PlayerId &&
               string.Equals(HeroName, other.HeroName, StringComparison.Ordinal) &&
               string.Equals(HeroCardId, other.HeroCardId, StringComparison.Ordinal) &&
               TavernTier == other.TavernTier &&
               Health == other.Health &&
               Armor == other.Armor &&
               Presence == other.Presence &&
               LastSeenTurn == other.LastSeenTurn &&
               TurnsAgo == other.TurnsAgo &&
               Triples == other.Triples &&
               Equals(Changes, other.Changes) &&
               ViewStateComparison.ListEquals(ProgressionRows, other.ProgressionRows) &&
               ViewStateComparison.ListEquals(Board, other.Board);
    }

    public override bool Equals(object? obj) => Equals(obj as OpponentOverlayViewState);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(PlayerId);
        hash.Add(HeroName, StringComparer.Ordinal);
        hash.Add(TavernTier);
        hash.Add(Health);
        hash.Add(Armor);
        hash.Add(Presence);
        hash.Add(LastSeenTurn);
        hash.Add(TurnsAgo);
        hash.Add(Triples);
        hash.Add(Changes);
        hash.Add(ViewStateComparison.ListHash(ProgressionRows));
        hash.Add(ViewStateComparison.ListHash(Board));
        return hash.ToHashCode();
    }
}
