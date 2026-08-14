namespace IceCrow.Presentation;

/// <summary>
/// Non-blocking lookup for already-available local card art. Presentation never
/// performs IO: the composition root owns caching and only publishes a path once
/// the file exists locally. A miss is expected and renders a placeholder.
/// </summary>
public interface ICardArtSource
{
    bool TryGetArtPath(string cardId, out string artPath);
}
