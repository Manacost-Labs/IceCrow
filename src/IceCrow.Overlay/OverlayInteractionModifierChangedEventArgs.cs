using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed class OverlayInteractionModifierChangedEventArgs(
    OverlayInteractionModifier modifier) : EventArgs
{
    public OverlayInteractionModifier Modifier { get; } = modifier;
}
