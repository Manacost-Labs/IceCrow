using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace IceCrow.ProfileSync.History.Decks;

/// <summary>An immutable exact deck revision. The key is stable but never shown to users.</summary>
public sealed record DeckRevisionIdentity(
    string Format,
    string? DeckCode,
    string? DeckHash)
{
    public const int MaximumDeckCodeLength = 4096;
    public const int MaximumDeckHashLength = 128;

    [JsonIgnore]
    public string Key => CreateKey(Format, DeckCode, DeckHash);

    public static DeckRevisionIdentity FromCode(string format, string deckCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(deckCode);
        var identity = new DeckRevisionIdentity(format, deckCode, null);
        identity.Validate();
        return identity;
    }

    internal static DeckRevisionIdentity? FromHistory(HistoryDeck deck)
    {
        var format = deck.Mode switch
        {
            HistoryGameMode.Standard => "standard",
            HistoryGameMode.Wild => "wild",
            _ => null,
        };
        if (format is null || (deck.DeckCode is null && deck.DeckHash is null))
        {
            return null;
        }

        var identity = new DeckRevisionIdentity(format, deck.DeckCode, deck.DeckHash);
        return identity.IsValid() ? identity : null;
    }

    internal static DeckRevisionIdentity? FromHistory(HistoryMatch match)
    {
        var format = match.Mode switch
        {
            HistoryGameMode.Standard => "standard",
            HistoryGameMode.Wild => "wild",
            _ => null,
        };
        if (format is null || (match.DeckCode is null && match.DeckHash is null))
        {
            return null;
        }

        var identity = new DeckRevisionIdentity(format, match.DeckCode, match.DeckHash);
        return identity.IsValid() ? identity : null;
    }

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidDataException("The deck revision identity is outside its contract limits.");
        }
    }

    internal bool IsValid() =>
        Format is "standard" or "wild" &&
        (DeckCode is { Length: > 0 and <= MaximumDeckCodeLength } ||
         DeckHash is { Length: > 0 and <= MaximumDeckHashLength }) &&
        (DeckCode is null || !DeckCode.Any(char.IsControl)) &&
        (DeckHash is null || !DeckHash.Any(char.IsControl));

    private static string CreateKey(string format, string? deckCode, string? deckHash)
    {
        var source = deckCode is not null ? $"code:{deckCode}" : $"hash:{deckHash}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{format}\0{source}"));
        return Convert.ToHexStringLower(bytes);
    }
}
