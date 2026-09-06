namespace IceCrow.Tracking.Constructed;

/// <summary>
/// The local player's visible mulligan. <see cref="Initial"/> is the hand at
/// <c>MULLIGAN_STATE=INPUT</c>, <see cref="Kept"/> and <see cref="Replaced"/>
/// partition it by whether each initial entity was still in hand at
/// <c>MULLIGAN_STATE=DONE</c>, and <see cref="After"/> is the whole hand at
/// DONE, which may include a card that was never offered (The Coin for the
/// second player). Certainty is Exact only when both transitions were seen
/// for a known local player; otherwise every list is empty.
/// </summary>
public sealed record ConstructedMulligan(
    IReadOnlyList<string> Initial,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> After,
    EvidenceCertainty Certainty)
{
    public static readonly ConstructedMulligan Unknown = new([], [], [], [], EvidenceCertainty.Unknown);
}
