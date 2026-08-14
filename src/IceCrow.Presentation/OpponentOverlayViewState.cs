namespace IceCrow.Presentation;

public sealed class OpponentOverlayViewState
{
    private readonly string[] _progressionRows;
    private readonly string[] _boardRows;

    public OpponentOverlayViewState(
        int playerId,
        string heroDisplay,
        string? heroCardId,
        string tavernTierLine,
        string statusLine,
        string lastSeenLine,
        string ageLine,
        IEnumerable<string> progressionRows,
        string triplesLine,
        IEnumerable<string> boardRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(heroDisplay);
        ArgumentNullException.ThrowIfNull(tavernTierLine);
        ArgumentNullException.ThrowIfNull(statusLine);
        ArgumentNullException.ThrowIfNull(lastSeenLine);
        ArgumentNullException.ThrowIfNull(ageLine);
        ArgumentNullException.ThrowIfNull(progressionRows);
        ArgumentNullException.ThrowIfNull(triplesLine);
        ArgumentNullException.ThrowIfNull(boardRows);

        PlayerId = playerId;
        HeroDisplay = heroDisplay;
        HeroCardId = heroCardId;
        TavernTierLine = tavernTierLine;
        StatusLine = statusLine;
        LastSeenLine = lastSeenLine;
        AgeLine = ageLine;
        _progressionRows = progressionRows.ToArray();
        ProgressionRows = Array.AsReadOnly(_progressionRows);
        TriplesLine = triplesLine;
        _boardRows = boardRows.ToArray();
        BoardRows = Array.AsReadOnly(_boardRows);
    }

    public int PlayerId { get; }

    public string HeroDisplay { get; }

    public string? HeroCardId { get; }

    public string TavernTierLine { get; }

    public string StatusLine { get; }

    public string LastSeenLine { get; }

    public string AgeLine { get; }

    public IReadOnlyList<string> ProgressionRows { get; }

    public string TriplesLine { get; }

    public IReadOnlyList<string> BoardRows { get; }
}
