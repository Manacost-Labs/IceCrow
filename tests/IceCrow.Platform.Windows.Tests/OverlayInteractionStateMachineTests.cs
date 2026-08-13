namespace IceCrow.Platform.Windows.Tests;

public sealed class OverlayInteractionStateMachineTests
{
    [Fact]
    public void StartsClickThroughAndBecomesInteractiveOnlyWhileAvailableAndHeld()
    {
        var stateMachine = new OverlayInteractionStateMachine();

        Assert.Equal(OverlayInteractionMode.ClickThrough, stateMachine.Mode);
        Assert.False(stateMachine.Update(modifierHeld: false, overlayAvailable: true).HasChanged);

        var held = stateMachine.Update(modifierHeld: true, overlayAvailable: true);

        Assert.True(held.HasChanged);
        Assert.Equal(OverlayInteractionMode.Interactive, held.Current);
        Assert.Equal(OverlayInteractionMode.Interactive, stateMachine.Mode);
    }

    [Fact]
    public void ReleasingModifierRestoresClickThrough()
    {
        var stateMachine = new OverlayInteractionStateMachine();
        _ = stateMachine.Update(modifierHeld: true, overlayAvailable: true);

        var released = stateMachine.Update(modifierHeld: false, overlayAvailable: true);

        Assert.True(released.HasChanged);
        Assert.Equal(OverlayInteractionMode.ClickThrough, released.Current);
    }

    [Fact]
    public void UnavailableOverlayCannotBecomeInteractive()
    {
        var stateMachine = new OverlayInteractionStateMachine();

        var transition = stateMachine.Update(modifierHeld: true, overlayAvailable: false);

        Assert.False(transition.HasChanged);
        Assert.Equal(OverlayInteractionMode.ClickThrough, stateMachine.Mode);
    }

    [Fact]
    public void LosingOverlayAvailabilityRestoresClickThroughEvenWhileModifierIsHeld()
    {
        var stateMachine = new OverlayInteractionStateMachine();
        _ = stateMachine.Update(modifierHeld: true, overlayAvailable: true);

        var unavailable = stateMachine.Update(
            modifierHeld: true,
            overlayAvailable: false);

        Assert.True(unavailable.HasChanged);
        Assert.Equal(OverlayInteractionMode.ClickThrough, unavailable.Current);
    }

    [Fact]
    public void FailSafeAlwaysRestoresClickThroughAndIsIdempotent()
    {
        var stateMachine = new OverlayInteractionStateMachine();
        _ = stateMachine.Update(modifierHeld: true, overlayAvailable: true);

        var first = stateMachine.FailSafe();
        var second = stateMachine.FailSafe();

        Assert.True(first.HasChanged);
        Assert.Equal(OverlayInteractionMode.ClickThrough, first.Current);
        Assert.False(second.HasChanged);
    }
}
