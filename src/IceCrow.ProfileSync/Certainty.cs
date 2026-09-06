namespace IceCrow.ProfileSync;

/// <summary>
/// Typed certainty of a collected fact, ordered so that combining two facts
/// with <see cref="CertaintyRules.Lowest"/> can only keep or reduce it.
/// </summary>
public enum Certainty
{
    Unknown = 0,
    Inferred = 1,
    Partial = 2,
    Exact = 3,
}

public static class CertaintyRules
{
    /// <summary>A derived fact is never more certain than its weakest input.</summary>
    public static Certainty Lowest(Certainty first, Certainty second) =>
        (Certainty)Math.Min((int)first, (int)second);
}
