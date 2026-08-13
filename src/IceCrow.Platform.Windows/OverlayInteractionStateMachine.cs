namespace IceCrow.Platform.Windows;

public enum OverlayInteractionMode
{
    ClickThrough,
    Interactive,
}

public readonly record struct OverlayInteractionTransition(
    OverlayInteractionMode Previous,
    OverlayInteractionMode Current)
{
    public bool HasChanged => Previous != Current;
}

public sealed class OverlayInteractionStateMachine
{
    public OverlayInteractionMode Mode { get; private set; } = OverlayInteractionMode.ClickThrough;

    public OverlayInteractionTransition Update(bool modifierHeld, bool overlayAvailable)
    {
        var desired = modifierHeld && overlayAvailable
            ? OverlayInteractionMode.Interactive
            : OverlayInteractionMode.ClickThrough;
        return TransitionTo(desired);
    }

    public OverlayInteractionTransition FailSafe() =>
        TransitionTo(OverlayInteractionMode.ClickThrough);

    private OverlayInteractionTransition TransitionTo(OverlayInteractionMode desired)
    {
        var transition = new OverlayInteractionTransition(Mode, desired);
        Mode = desired;
        return transition;
    }
}
