using System.Globalization;

namespace IceCrow.Presentation;

/// <summary>What happened to one minion between the two most recent fights.</summary>
public enum MinionChangeKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>
/// One ready-to-render row of the "since last fight" list. All strings are
/// precomputed so the overlay only places text.
/// </summary>
public sealed record MinionChangeViewState(
    MinionChangeKind Kind,
    string DisplayName,
    string? TransitionLine,
    string? DeltaLine)
{
    /// <summary>Leading glyph: <c>+</c> for added, <c>−</c> for removed.</summary>
    public string Marker => Kind switch
    {
        MinionChangeKind.Added => "+",
        MinionChangeKind.Removed => "−",
        _ => string.Empty,
    };

    public static MinionChangeViewState Added(string displayName, int attack, int health) =>
        new(MinionChangeKind.Added, Validated(displayName), FormatStats(attack, health), null);

    public static MinionChangeViewState Removed(string displayName) =>
        new(MinionChangeKind.Removed, Validated(displayName), null, null);

    public static MinionChangeViewState Changed(
        string displayName,
        int previousAttack,
        int previousHealth,
        int currentAttack,
        int currentHealth) =>
        new(
            MinionChangeKind.Changed,
            Validated(displayName),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatStats(previousAttack, previousHealth)} → {FormatStats(currentAttack, currentHealth)}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatSigned(currentAttack - previousAttack)}/{FormatSigned(currentHealth - previousHealth)}"));

    private static string Validated(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return displayName;
    }

    private static string FormatStats(int attack, int health) =>
        string.Create(CultureInfo.InvariantCulture, $"{attack}/{health}");

    private static string FormatSigned(int delta) => delta >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"+{delta}")
        : string.Create(CultureInfo.InvariantCulture, $"−{-delta}");
}
