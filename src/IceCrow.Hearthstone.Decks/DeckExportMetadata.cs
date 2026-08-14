namespace IceCrow.Hearthstone.Decks;

public sealed record DeckExportMetadata(string? Name, IReadOnlyList<string> Comments)
{
    public static DeckExportMetadata Empty { get; } = new(null, Array.Empty<string>());
}
