using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IceCrow.Presentation;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// Compact minion tile: art, attack, health. Name and tier stay secondary, and a
/// missing image renders a deliberate placeholder rather than a broken frame.
/// </summary>
public sealed class MinionTile : Control
{
    private static readonly DependencyPropertyKey ArtSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ArtSource),
            typeof(ImageSource),
            typeof(MinionTile),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ArtSourceProperty =
        ArtSourcePropertyKey.DependencyProperty;

    public static readonly DependencyProperty ViewStateProperty =
        DependencyProperty.Register(
            nameof(ViewState),
            typeof(MinionTileViewState),
            typeof(MinionTile),
            new PropertyMetadata(null, OnViewStateChanged));

    private int _artGeneration;

    static MinionTile()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MinionTile),
            new FrameworkPropertyMetadata(typeof(MinionTile)));
    }

    public MinionTile()
    {
        Loaded += OnLoaded;
    }

    public MinionTileViewState? ViewState
    {
        get => (MinionTileViewState?)GetValue(ViewStateProperty);
        set => SetValue(ViewStateProperty, value);
    }

    /// <summary>Decoded art, or <see langword="null"/> while the placeholder is shown.</summary>
    public ImageSource? ArtSource => (ImageSource?)GetValue(ArtSourceProperty);

    private static void OnViewStateChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        _ = eventArgs;
        ((MinionTile)sender).RefreshArt();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RefreshArt();
    }

    private void RefreshArt()
    {
        // Every refresh invalidates in-flight loads so a recycled tile never
        // shows the previous minion's art.
        var generation = unchecked(++_artGeneration);
        SetValue(ArtSourcePropertyKey, null);

        if (!IsLoaded ||
            ViewState?.ArtImagePath is not string artPath ||
            OverlayVisuals.GetImageCache(this) is not OverlayImageCache cache)
        {
            return;
        }

        var decodeWidth = OverlayLayout.ArtDecodeWidth(
            OverlayLayout.MinionTileWidth(OverlayVisuals.GetLayoutMode(this)),
            VisualTreeHelper.GetDpi(this).DpiScaleX);
        _ = LoadArtAsync(cache, artPath, decodeWidth, generation);
    }

    private async Task LoadArtAsync(
        OverlayImageCache cache,
        string artPath,
        int decodeWidth,
        int generation)
    {
        try
        {
            // A cache hit completes synchronously and never queues dispatcher work.
            var decoded = await cache.GetAsync(artPath, decodeWidth).ConfigureAwait(true);
            if (decoded is not null && generation == _artGeneration)
            {
                SetValue(ArtSourcePropertyKey, decoded);
            }
        }
        catch (Exception exception)
        {
            // Art is optional enrichment: a failure must never break the overlay.
            Debug.WriteLine(exception);
        }
    }
}
