namespace IceCrow.Hearthstone.Entities;

// The GameEntity STATE tag. Values verified against the HearthDb State enum
// on 2026-09-06.
public enum GameLifecycleState
{
    Invalid = 0,
    Loading = 1,
    Running = 2,
    Complete = 3,
}
