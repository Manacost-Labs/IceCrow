using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// The complete animation vocabulary of the overlay. Every animation is short,
/// finite, transform/opacity only, and is skipped when optional effects are off.
/// </summary>
/// <remarks>
/// Animations are started imperatively rather than kept in retained storyboards
/// so no control stays reachable through an animation timeline, and so a
/// disabled effect costs nothing instead of running into a suppressed trigger.
/// </remarks>
internal static class OverlayAnimations
{
    private static readonly Duration EntranceDuration =
        new(TimeSpan.FromMilliseconds(140));

    private static readonly Duration EventPulseDuration =
        new(TimeSpan.FromMilliseconds(240));

    private const double EntranceOffset = 6;

    public static void PlayEntrance(FrameworkElement element, OverlayRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!OverlayVisuals.GetEffectsEnabled(element))
        {
            return;
        }

        var translate = EnsureTranslateTransform(element);
        diagnostics.RecordAnimationStarted();

        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, EntranceDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(EntranceOffset, 0, EntranceDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }

    /// <summary>
    /// Brief accent flash for an important transient event. It decays back to the
    /// element's own opacity, so nothing stays animated after it completes.
    /// </summary>
    public static void PlayEventPulse(
        FrameworkElement element,
        double restingOpacity,
        OverlayRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!OverlayVisuals.GetEffectsEnabled(element))
        {
            return;
        }

        diagnostics.RecordAnimationStarted();
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, restingOpacity, EventPulseDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
        {
            return existing;
        }

        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }
}
