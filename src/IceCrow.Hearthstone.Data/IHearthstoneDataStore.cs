namespace IceCrow.Hearthstone.Data;

public interface IHearthstoneDataStore
{
    Task<HearthstoneDataSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(HearthstoneDataSnapshot snapshot, CancellationToken cancellationToken = default);
}
