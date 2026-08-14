using System.Windows;
using IceCrow.Presentation;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// Inherited rendering context for an overlay element tree. Setting these on a
/// window root is how Performance Mode, responsive density, and the image cache
/// reach every IceCrow control without a global singleton or an event bus.
/// </summary>
public static class OverlayVisuals
{
    /// <summary>
    /// Enables optional visuals: short entrance transitions and the brief
    /// important-event pulse. Never required for the design to be readable.
    /// </summary>
    public static readonly DependencyProperty EffectsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "EffectsEnabled",
            typeof(bool),
            typeof(OverlayVisuals),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Responsive density derived from the Hearthstone client size.</summary>
    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.RegisterAttached(
            "LayoutMode",
            typeof(OverlayLayoutMode),
            typeof(OverlayVisuals),
            new FrameworkPropertyMetadata(
                OverlayLayoutMode.Regular,
                FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Shared decoded-image cache, or <see langword="null"/> for placeholders only.</summary>
    public static readonly DependencyProperty ImageCacheProperty =
        DependencyProperty.RegisterAttached(
            "ImageCache",
            typeof(OverlayImageCache),
            typeof(OverlayVisuals),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetEffectsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EffectsEnabledProperty, value);
    }

    public static bool GetEffectsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EffectsEnabledProperty);
    }

    public static void SetLayoutMode(DependencyObject element, OverlayLayoutMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LayoutModeProperty, value);
    }

    public static OverlayLayoutMode GetLayoutMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (OverlayLayoutMode)element.GetValue(LayoutModeProperty);
    }

    public static void SetImageCache(DependencyObject element, OverlayImageCache? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ImageCacheProperty, value);
    }

    public static OverlayImageCache? GetImageCache(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (OverlayImageCache?)element.GetValue(ImageCacheProperty);
    }
}
