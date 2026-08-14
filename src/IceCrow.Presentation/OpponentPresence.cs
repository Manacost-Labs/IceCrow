namespace IceCrow.Presentation;

/// <summary>
/// Describes what IceCrow knows about an opponent board, independent of styling.
/// </summary>
public enum OpponentPresence
{
    /// <summary>The opponent has never been fought this lobby.</summary>
    NotFought,

    /// <summary>The opponent was fought, but the recorded board had no minions.</summary>
    EmptyBoard,

    /// <summary>A board with minions was recorded.</summary>
    Seen,
}
