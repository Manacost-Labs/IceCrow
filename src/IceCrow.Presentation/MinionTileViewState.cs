using System.Globalization;

namespace IceCrow.Presentation;

/// <summary>
/// One compact minion tile: art, attack, health. Name and tier are secondary and
/// are only shown in the detail surface or a larger layout.
/// </summary>
public sealed record MinionTileViewState(
    int BoardPosition,
    string? CardId,
    string DisplayName,
    string Initials,
    int Attack,
    int Health,
    int? TavernTier,
    string? ArtImagePath)
{
    private const int MaximumInitials = 2;

    public string StatLine => string.Create(
        CultureInfo.InvariantCulture,
        $"{Attack}/{Health}");

    public string TierBadge => TavernTier is int tier
        ? string.Create(CultureInfo.InvariantCulture, $"T{tier}")
        : string.Empty;

    public static MinionTileViewState Create(
        int boardPosition,
        string? cardId,
        string displayName,
        int attack,
        int health,
        int? tavernTier,
        string? artImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new MinionTileViewState(
            boardPosition,
            cardId,
            displayName,
            CreateInitials(displayName),
            attack,
            health,
            tavernTier,
            artImagePath);
    }

    /// <summary>
    /// Placeholder glyph used when art is not available locally. Missing art must
    /// still look intentional, so the tile shows initials rather than a broken image.
    /// </summary>
    private static string CreateInitials(string displayName)
    {
        Span<char> initials = stackalloc char[MaximumInitials];
        var written = 0;
        var atWordStart = true;

        foreach (var character in displayName)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '\'')
            {
                atWordStart = true;
                continue;
            }

            if (atWordStart && char.IsLetterOrDigit(character))
            {
                initials[written++] = char.ToUpperInvariant(character);
                if (written == MaximumInitials)
                {
                    break;
                }
            }

            atWordStart = false;
        }

        return written == 0 ? "?" : new string(initials[..written]);
    }
}
