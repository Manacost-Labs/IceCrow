namespace IceCrow.Hearthstone.Data;

public sealed record CardImageInfo(
    Uri? Card,
    Uri? Golden,
    Uri? Art,
    Uri? Framed,
    Uri? Horizontal)
{
    public static CardImageInfo Empty { get; } = new(null, null, null, null, null);
}
