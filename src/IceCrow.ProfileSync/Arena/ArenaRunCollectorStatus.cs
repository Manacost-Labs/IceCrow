using IceCrow.Hearthstone.ClientState;

namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Bounded diagnostics for the Arena collector. <see cref="GapCount"/> counts
/// deck transitions of the current run that could not be attributed to a
/// single offered card; such picks are never guessed.
/// </summary>
public sealed record ArenaRunCollectorStatus(
    ClientStateProviderStatus Availability,
    Guid? CurrentRunId,
    bool IsDrafting,
    int PickCount,
    int GapCount,
    long ObservedSnapshots,
    long EmittedEvents,
    long RunsStarted)
{
    public static readonly ArenaRunCollectorStatus Initial =
        new(ClientStateProviderStatus.Unavailable, null, false, 0, 0, 0, 0, 0);
}
