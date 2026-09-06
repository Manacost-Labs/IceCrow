namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// One read-only view of the current client for a single snapshot type.
/// <see cref="ReadAsync"/> returns null whenever the client state is
/// unavailable (no process, no adapter, screen not open); absence never throws.
/// A licensed adapter implements the named sources below inside the isolated
/// HearthMirror boundary; everything else consumes only these contracts.
/// </summary>
public interface IClientStateSource<TSnapshot>
    where TSnapshot : class
{
    ClientStateProviderStatus Status { get; }

    ValueTask<TSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
}

public interface ISelectedDeckSource : IClientStateSource<SelectedDeckSnapshot>
{
}

public interface ICollectionSource : IClientStateSource<CollectionSnapshot>
{
}

public interface IArenaClientSource : IClientStateSource<ArenaClientSnapshot>
{
}

public interface IBattlegroundsRatingSource : IClientStateSource<BattlegroundsRatingSnapshot>
{
}
