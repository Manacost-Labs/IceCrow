namespace IceCrow.ProfileSync.Records;

/// <summary>Own or opponent deck evidence; a code is present only when exact.</summary>
public sealed record DeckEvidence(
    string? DeckCode,
    string? DeckHash,
    Certainty Confidence)
{
    public static readonly DeckEvidence Unknown = new(null, null, Certainty.Unknown);
}

public sealed record MulliganRecord(
    IReadOnlyList<string> Initial,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> After,
    Certainty Confidence)
{
    public static readonly MulliganRecord Unknown = new([], [], [], [], Certainty.Unknown);
}

/// <summary>
/// The opponent deck as observed from the client: card ids seen played or
/// revealed. A deck code is never produced client-side; HearthPulse may fill
/// it only from the opponent own exact submission for the same game.
/// </summary>
public sealed record OpponentDeckEvidence(
    IReadOnlyList<string> ObservedCards,
    string? DeckCode,
    string? DeckHash,
    Certainty Confidence)
{
    public static readonly OpponentDeckEvidence Unknown = new([], null, null, Certainty.Unknown);
}
