namespace IceCrow.Platform.Windows;

public static class ModifierKeyStateReader
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int KeyDownMask = 0x8000;

    public static bool IsHeld(OverlayInteractionModifier modifier)
    {
        var virtualKey = modifier switch
        {
            OverlayInteractionModifier.Alt => VkMenu,
            OverlayInteractionModifier.Control => VkControl,
            OverlayInteractionModifier.Shift => VkShift,
            _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null),
        };

        return (NativeMethods.GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;
    }
}
