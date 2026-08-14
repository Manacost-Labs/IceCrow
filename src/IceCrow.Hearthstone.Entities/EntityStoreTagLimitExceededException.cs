namespace IceCrow.Hearthstone.Entities;

public sealed class EntityStoreTagLimitExceededException : InvalidOperationException
{
    internal EntityStoreTagLimitExceededException(int maximumTags)
        : base($"Entity store exceeds the {maximumTags} total-tag limit.")
    {
        MaximumTags = maximumTags;
    }

    public int MaximumTags { get; }
}
