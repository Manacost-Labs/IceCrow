namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Why the collection was read. The collection is never polled: every read
/// is tied to one of these explicit moments.
/// </summary>
public enum CollectionRefreshTrigger
{
    Startup,
    CollectionManagerExit,
    PackOpeningExit,
    Manual,
}
