namespace IceCrow.Presentation;

/// <summary>
/// How current the recorded opponent board information is, in whole turns.
/// Semantic only: the overlay decides how each level looks.
/// </summary>
public enum OpponentStaleness
{
    /// <summary>Observed this turn.</summary>
    Fresh,

    /// <summary>One or two turns old; usually still representative.</summary>
    Recent,

    /// <summary>Three or more turns old; the board has likely moved on.</summary>
    Stale,

    /// <summary>Six or more turns old; treat as historical background only.</summary>
    VeryStale,
}
