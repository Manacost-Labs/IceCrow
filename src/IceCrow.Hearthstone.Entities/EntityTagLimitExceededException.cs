namespace IceCrow.Hearthstone.Entities;

public sealed class EntityTagLimitExceededException : InvalidOperationException
{
    internal EntityTagLimitExceededException(int entityId, int maximumTags)
        : base($"Entity {entityId} exceeds the {maximumTags} tag limit.")
    {
        EntityId = entityId;
        MaximumTags = maximumTags;
    }

    public int EntityId { get; }

    public int MaximumTags { get; }
}
