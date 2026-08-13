namespace IceCrow.Hearthstone.Entities;

public sealed record EntityMutation(
    int EntityId,
    GameTag Tag,
    int PreviousValue,
    int Value);
