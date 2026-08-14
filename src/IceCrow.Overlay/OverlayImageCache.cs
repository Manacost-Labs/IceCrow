using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IceCrow.Overlay;

/// <summary>
/// Bounded decoded-image cache for overlay thumbnails.
/// </summary>
/// <remarks>
/// <para>
/// The overlay never blocks a render pass on disk. A miss returns immediately,
/// the tile shows its placeholder, and a background decode fills the cache.
/// </para>
/// <para>
/// Instances are affine to the WPF dispatcher that calls them: every mutation
/// happens either before the first await or on the resumed UI context.
/// </para>
/// </remarks>
public sealed class OverlayImageCache
{
    private const int DefaultCapacity = 128;
    private const int MaximumConcurrentDecodes = 8;

    private readonly Dictionary<ImageKey, ImageSource> _decoded = [];
    private readonly Queue<ImageKey> _evictionOrder = new();
    private readonly Dictionary<ImageKey, Task<BitmapImage?>> _pending = [];
    private readonly int _capacity;

    public OverlayImageCache(OverlayRenderDiagnostics diagnostics, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Diagnostics = diagnostics;
        _capacity = capacity;
    }

    public OverlayRenderDiagnostics Diagnostics { get; }

    /// <summary>
    /// Returns the decoded image, decoding it in the background on a miss.
    /// Concurrent requests for the same key share one decode, and the number of
    /// concurrent decodes is bounded so rapid hovering cannot fan out.
    /// </summary>
    public async Task<ImageSource?> GetAsync(string artPath, int decodeWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodeWidth);

        var key = new ImageKey(artPath, decodeWidth);
        if (_decoded.TryGetValue(key, out var cached))
        {
            Diagnostics.RecordImageCacheHit();
            return cached;
        }

        Diagnostics.RecordImageCacheMiss();
        if (!_pending.TryGetValue(key, out var decode))
        {
            if (_pending.Count >= MaximumConcurrentDecodes)
            {
                return null;
            }

            Diagnostics.RecordImageDecode();
            decode = Task.Run(() => Decode(key));
            _pending[key] = decode;
        }

        var decoded = await decode.ConfigureAwait(true);
        _pending.Remove(key);
        if (decoded is null)
        {
            return null;
        }

        Store(key, decoded);
        return decoded;
    }

    private void Store(ImageKey key, ImageSource decoded)
    {
        if (!_decoded.TryAdd(key, decoded))
        {
            return;
        }

        _evictionOrder.Enqueue(key);
        while (_evictionOrder.Count > _capacity)
        {
            _decoded.Remove(_evictionOrder.Dequeue());
        }
    }

    private static BitmapImage? Decode(ImageKey key)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(key.ArtPath), UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = key.DecodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or
                UriFormatException or
                NotSupportedException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            // Missing or unreadable art is expected: the tile keeps its placeholder.
            return null;
        }
    }

    private readonly record struct ImageKey(string ArtPath, int DecodeWidth);
}
