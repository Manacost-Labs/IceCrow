namespace IceCrow.Infrastructure.ManacostApi;

public interface ICardImageResolver
{
    ValueTask<string?> GetCachedPathAsync(Uri imageUri, CancellationToken cancellationToken = default);

    Task<string?> ResolveAsync(Uri imageUri, CancellationToken cancellationToken = default);
}
