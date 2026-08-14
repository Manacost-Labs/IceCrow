namespace IceCrow.Presentation;

/// <summary>
/// Immutable rendering policy for the overlay. Performance Mode reduces optional
/// visual work; it never changes layout, hierarchy, or which information is shown.
/// </summary>
public sealed record OverlayRenderingSettings(bool PerformanceMode)
{
    /// <summary>Normal mode. Already inexpensive: no blur, no continuous animation.</summary>
    public static OverlayRenderingSettings Default { get; } = new(PerformanceMode: false);

    /// <summary>Extra conservative mode for low-end machines.</summary>
    public static OverlayRenderingSettings Performance { get; } = new(PerformanceMode: true);

    /// <summary>Single lightweight shadow on top-level floating panels.</summary>
    public bool AllowPanelShadow => !PerformanceMode;

    /// <summary>Short finite entrance transitions for panels that become visible.</summary>
    public bool AllowEntranceAnimation => !PerformanceMode;

    /// <summary>Brief accent pulse for important transient events.</summary>
    public bool AllowEventPulse => !PerformanceMode;
}
