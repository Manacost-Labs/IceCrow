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
/// How reliable the identity behind a change row is. Presentation may simplify
/// wording, but it must never display more certainty than the domain observed.
/// </summary>
public enum MinionChangeConfidence
{
    /// <summary>The same entity was observed in both fights.</summary>
    Exact,

    /// <summary>A unique card copy links the fights; the entity was recreated.</summary>
    Likely,

    /// <summary>Duplicate copies made the pairing arbitrary; only the current observation is factual.</summary>
    Ambiguous,
}

/// <summary>
/// One ready-to-render row of the "since last fight" list. All strings are
/// precomputed so the overlay only places text.
/// </summary>
public sealed record MinionChangeViewState(
    MinionChangeKind Kind,
    MinionChangeConfidence Confidence,
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
        new(
            MinionChangeKind.Added,
            MinionChangeConfidence.Exact,
            Validated(displayName),
            FormatStats(attack, health),
            null);

    public static MinionChangeViewState Removed(string displayName) =>
        new(
            MinionChangeKind.Removed,
            MinionChangeConfidence.Exact,
            Validated(displayName),
            null,
            null);

    public static MinionChangeViewState Changed(
        string displayName,
        int previousAttack,
        int previousHealth,
        int currentAttack,
        int currentHealth,
        MinionChangeConfidence confidence)
    {
        // An ambiguous pairing may only state the current observation; a
        // "from → to" line would present an arbitrary pairing as a fact.
        var transition = confidence == MinionChangeConfidence.Ambiguous
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"now {FormatStats(currentAttack, currentHealth)}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatStats(previousAttack, previousHealth)} → {FormatStats(currentAttack, currentHealth)}");
        var delta = confidence == MinionChangeConfidence.Ambiguous
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(confidence == MinionChangeConfidence.Likely ? "~" : string.Empty)}{FormatSigned(currentAttack - previousAttack)}/{FormatSigned(currentHealth - previousHealth)}");

        return new MinionChangeViewState(
            MinionChangeKind.Changed,
            confidence,
            Validated(displayName),
            transition,
            delta);
    }

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
