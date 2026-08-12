using IceCrow.Platform.Windows;

namespace IceCrow.Platform.Windows.Tests;

public sealed class OverlayBoundsCalculatorTests
{
    [Fact]
    public void ToLogicalBoundsPreservesPixelsAtOneHundredPercentDpi()
    {
        var nativeBounds = new NativeClientBounds(40, 80, 1600, 900);

        var result = OverlayBoundsCalculator.ToLogicalBounds(nativeBounds, 1, 1);

        Assert.Equal(new OverlayLogicalBounds(40, 80, 1600, 900), result);
    }

    [Fact]
    public void ToLogicalBoundsScalesPositionAndSizeAtOneHundredFiftyPercentDpi()
    {
        var nativeBounds = new NativeClientBounds(300, 150, 1536, 864);

        var result = OverlayBoundsCalculator.ToLogicalBounds(nativeBounds, 1.5, 1.5);

        Assert.Equal(new OverlayLogicalBounds(200, 100, 1024, 576), result);
    }

    [Fact]
    public void ToLogicalBoundsUsesIndependentHorizontalAndVerticalScales()
    {
        var nativeBounds = new NativeClientBounds(-240, 300, 2400, 1800);

        var result = OverlayBoundsCalculator.ToLogicalBounds(nativeBounds, 1.25, 1.5);

        Assert.Equal(new OverlayLogicalBounds(-192, 200, 1920, 1200), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToLogicalBoundsRejectsInvalidHorizontalScale(double dpiScaleX)
    {
        var nativeBounds = new NativeClientBounds(0, 0, 1600, 900);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayBoundsCalculator.ToLogicalBounds(nativeBounds, dpiScaleX, 1));
    }

    [Fact]
    public void ToLogicalBoundsRejectsInvalidVerticalScale()
    {
        var nativeBounds = new NativeClientBounds(0, 0, 1600, 900);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayBoundsCalculator.ToLogicalBounds(nativeBounds, 1, 0));
    }
}
