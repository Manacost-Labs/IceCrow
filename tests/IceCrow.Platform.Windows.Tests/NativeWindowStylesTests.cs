namespace IceCrow.Platform.Windows.Tests;

public sealed class NativeWindowStylesTests
{
    [Fact]
    public void ClickThroughPolicyAddsTransparentNoActivateAndToolWindow()
    {
        const long existingStyle = 0x00040000;

        var result = NativeWindowStyles.CalculateOverlayExtendedStyles(
            existingStyle,
            isClickThrough: true);

        Assert.Equal(existingStyle, result & existingStyle);
        Assert.NotEqual(0, result & NativeWindowStyles.WsExTransparent);
        Assert.NotEqual(0, result & NativeWindowStyles.WsExNoActivate);
        Assert.NotEqual(0, result & NativeWindowStyles.WsExToolWindow);
    }

    [Fact]
    public void InteractivePolicyRemovesOnlyTransparentAndKeepsNoActivate()
    {
        const long existingStyle = 0x00040000 |
                                   NativeWindowStyles.WsExTransparent;

        var result = NativeWindowStyles.CalculateOverlayExtendedStyles(
            existingStyle,
            isClickThrough: false);

        Assert.Equal(0, result & NativeWindowStyles.WsExTransparent);
        Assert.NotEqual(0, result & NativeWindowStyles.WsExNoActivate);
        Assert.NotEqual(0, result & NativeWindowStyles.WsExToolWindow);
        Assert.Equal(0x00040000, result & 0x00040000);
    }
}
