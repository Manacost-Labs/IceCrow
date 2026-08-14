namespace IceCrow.Presentation.Tests;

public sealed class OverlayDesignSystemTests
{
    [Theory]
    [InlineData(1024, OverlayLayoutMode.Compact)]
    [InlineData(OverlayLayout.CompactMaximumWidth, OverlayLayoutMode.Compact)]
    [InlineData(1280, OverlayLayoutMode.Regular)]
    [InlineData(2560, OverlayLayoutMode.Regular)]
    public void LayoutModeFollowsClientWidthBreakpoint(double width, OverlayLayoutMode expected) =>
        Assert.Equal(expected, OverlayLayout.FromClientWidth(width));

    [Fact]
    public void CompactLayoutShrinksThumbnailsAndPanels()
    {
        Assert.True(
            OverlayLayout.MinionTileWidth(OverlayLayoutMode.Compact) <
            OverlayLayout.MinionTileWidth(OverlayLayoutMode.Regular));
        Assert.True(
            OverlayLayout.DetailPanelWidth(OverlayLayoutMode.Compact) <
            OverlayLayout.DetailPanelWidth(OverlayLayoutMode.Regular));
    }

    [Theory]
    [InlineData(46, 1.0, 64)]
    [InlineData(58, 1.0, 64)]
    [InlineData(58, 1.5, 96)]
    [InlineData(58, 2.0, 128)]
    public void ArtDecodeWidthSnapsToSharedStepsNearDisplaySize(
        double tileWidth,
        double dpiScale,
        int expected) =>
        Assert.Equal(expected, OverlayLayout.ArtDecodeWidth(tileWidth, dpiScale));

    [Fact]
    public void PerformanceModeDisablesOptionalVisualsOnly()
    {
        Assert.True(OverlayRenderingSettings.Default.AllowPanelShadow);
        Assert.True(OverlayRenderingSettings.Default.AllowEntranceAnimation);
        Assert.True(OverlayRenderingSettings.Default.AllowEventPulse);

        Assert.False(OverlayRenderingSettings.Performance.AllowPanelShadow);
        Assert.False(OverlayRenderingSettings.Performance.AllowEntranceAnimation);
        Assert.False(OverlayRenderingSettings.Performance.AllowEventPulse);
    }

    [Theory]
    [InlineData("Reno Jackson", "RJ")]
    [InlineData("Alleycat", "A")]
    [InlineData("Sr. Tomb Diver", "ST")]
    [InlineData("13", "1")]
    public void MinionTilePlaceholderUsesReadableInitials(string displayName, string expected) =>
        Assert.Equal(
            expected,
            MinionTileViewState.Create(1, null, displayName, 1, 1, null, null).Initials);

    [Fact]
    public void MinionTilesWithTheSameValuesCompareEqual()
    {
        var first = MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null);
        var second = MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null);

        Assert.Equal(first, second);
        Assert.NotEqual(
            first,
            MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 5, 1, null));
    }
}
