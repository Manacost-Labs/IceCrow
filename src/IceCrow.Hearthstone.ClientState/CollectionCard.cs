namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// Owned copies of one collectible card as reported by the client. Optional
/// counts stay null when the client does not expose that finish at all.
/// </summary>
public sealed record CollectionCard
{
    public const int MaximumCount = 99;

    public CollectionCard(
        string cardId,
        int normalCount,
        int goldenCount,
        int? signatureCount,
        int? diamondCount)
    {
        ClientCardIds.ValidateCardId(cardId, nameof(cardId));
        CardId = cardId;
        NormalCount = ValidateCount(normalCount, nameof(normalCount));
        GoldenCount = ValidateCount(goldenCount, nameof(goldenCount));
        SignatureCount = ClientCardIds.ValidateOptionalRange(signatureCount, 0, MaximumCount, nameof(signatureCount));
        DiamondCount = ClientCardIds.ValidateOptionalRange(diamondCount, 0, MaximumCount, nameof(diamondCount));
    }

    public string CardId { get; }

    public int NormalCount { get; }

    public int GoldenCount { get; }

    public int? SignatureCount { get; }

    public int? DiamondCount { get; }

    private static int ValidateCount(int value, string parameterName)
    {
        if (value is < 0 or > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Card counts must be between 0 and {MaximumCount}.");
        }

        return value;
    }
}
