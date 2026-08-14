namespace IceCrow.Presentation;

/// <summary>
/// Overlay density derived from the Hearthstone client size. The overlay never
/// scales itself as one bitmap; it drops secondary text and shrinks thumbnails.
/// </summary>
public enum OverlayLayoutMode
{
    /// <summary>Small Hearthstone client: core information only.</summary>
    Compact,

    /// <summary>Normal Hearthstone client: core plus secondary information.</summary>
    Regular,
}

/// <summary>
/// Breakpoints and sizes shared by the overlay and the developer design preview.
/// Values are device-independent units, so they stay correct at 100–200% DPI.
/// </summary>
public static class OverlayLayout
{
    /// <summary>Logical client widths at or below this value use the compact layout.</summary>
    public const double CompactMaximumWidth = 1180;

    /// <summary>Minion tile edge in the compact layout.</summary>
    public const double CompactMinionTileWidth = 46;

    /// <summary>Minion tile edge in the regular layout.</summary>
    public const double RegularMinionTileWidth = 58;

    /// <summary>Opponent row width in the compact layout.</summary>
    public const double CompactOpponentRowWidth = 168;

    /// <summary>Opponent row width in the regular layout.</summary>
    public const double RegularOpponentRowWidth = 196;

    /// <summary>Detail panel width in the compact layout.</summary>
    public const double CompactDetailPanelWidth = 288;

    /// <summary>Detail panel width in the regular layout.</summary>
    public const double RegularDetailPanelWidth = 348;

    public static OverlayLayoutMode FromClientWidth(double logicalClientWidth) =>
        logicalClientWidth > CompactMaximumWidth
            ? OverlayLayoutMode.Regular
            : OverlayLayoutMode.Compact;

    public static double MinionTileWidth(OverlayLayoutMode mode) =>
        mode == OverlayLayoutMode.Compact
            ? CompactMinionTileWidth
            : RegularMinionTileWidth;

    public static double OpponentRowWidth(OverlayLayoutMode mode) =>
        mode == OverlayLayoutMode.Compact
            ? CompactOpponentRowWidth
            : RegularOpponentRowWidth;

    public static double DetailPanelWidth(OverlayLayoutMode mode) =>
        mode == OverlayLayoutMode.Compact
            ? CompactDetailPanelWidth
            : RegularDetailPanelWidth;

    /// <summary>
    /// Decode width for minion art. Art is decoded near its display size and
    /// rounded up to a small set of steps so a resize reuses cached decodes.
    /// </summary>
    public static int ArtDecodeWidth(double tileWidth, double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        var requested = tileWidth * dpiScale;
        return requested switch
        {
            <= 64 => 64,
            <= 96 => 96,
            <= 128 => 128,
            _ => 192,
        };
    }
}
