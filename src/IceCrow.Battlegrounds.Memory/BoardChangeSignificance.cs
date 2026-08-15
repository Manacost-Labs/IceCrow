namespace IceCrow.Battlegrounds.Memory;

/// <summary>
/// How much an opponent board changed between two observations. This is a
/// deterministic observation summary, not a threat or strategy estimate.
/// </summary>
public enum BoardChangeSignificance
{
    /// <summary>Both observations describe the same board.</summary>
    NoChange,

    /// <summary>Small roster or stat adjustments.</summary>
    Minor,

    /// <summary>Multiple roster changes or large total stat growth.</summary>
    Major,
}
