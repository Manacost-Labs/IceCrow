namespace IceCrow.Hearthstone.Entities;

// Values verified against the HearthDb Mulligan enum on 2026-09-06.
public enum MulliganState
{
    Invalid = 0,
    Input = 1,
    Dealing = 2,
    Waiting = 3,
    Done = 4,
    Refreshing = 5,
    PreRefreshing = 6,
}
