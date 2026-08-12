namespace IceCrow.Platform.Windows;

public readonly record struct OverlayLogicalBounds(double Left, double Top, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public static class OverlayBoundsCalculator
{
    public static OverlayLogicalBounds ToLogicalBounds(
        NativeClientBounds nativeBounds,
        double dpiScaleX,
        double dpiScaleY)
    {
        ValidateScale(dpiScaleX, nameof(dpiScaleX));
        ValidateScale(dpiScaleY, nameof(dpiScaleY));

        return new OverlayLogicalBounds(
            nativeBounds.Left / dpiScaleX,
            nativeBounds.Top / dpiScaleY,
            nativeBounds.Width / dpiScaleX,
            nativeBounds.Height / dpiScaleY);
    }

    private static void ValidateScale(double scale, string parameterName)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, scale, "DPI scale must be finite and greater than zero.");
        }
    }
}
