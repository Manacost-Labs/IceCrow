using System.Windows;
using System.Windows.Controls;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// The overlay surface primitive: nearly opaque background, one hairline border,
/// modest radius, and an optional angular ice-cut notch on the leading edge.
/// The notch is the single IceCrow signature element.
/// </summary>
public sealed class IcePanel : ContentControl
{
    private const string SignaturePartName = "PART_Signature";
    private const double SignatureRestingOpacity = 0.6;

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(IcePanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TrailingProperty =
        DependencyProperty.Register(
            nameof(Trailing),
            typeof(object),
            typeof(IcePanel),
            new PropertyMetadata(null));

    /// <summary>Shows the IceCrow notch. Reserved for the panel that currently matters.</summary>
    public static readonly DependencyProperty IsSignatureProperty =
        DependencyProperty.Register(
            nameof(IsSignature),
            typeof(bool),
            typeof(IcePanel),
            new PropertyMetadata(false));

    /// <summary>
    /// Applies the single allowed drop shadow. Only top-level floating panels set
    /// this, and Performance Mode clears it.
    /// </summary>
    public static readonly DependencyProperty HasElevationProperty =
        DependencyProperty.Register(
            nameof(HasElevation),
            typeof(bool),
            typeof(IcePanel),
            new PropertyMetadata(false));

    private FrameworkElement? _signature;

    static IcePanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IcePanel),
            new FrameworkPropertyMetadata(typeof(IcePanel)));
    }

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public bool IsSignature
    {
        get => (bool)GetValue(IsSignatureProperty);
        set => SetValue(IsSignatureProperty, value);
    }

    public bool HasElevation
    {
        get => (bool)GetValue(HasElevationProperty);
        set => SetValue(HasElevationProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _signature = GetTemplateChild(SignaturePartName) as FrameworkElement;
    }

    /// <summary>
    /// Short accent flash on the signature notch. Used only for important
    /// transient events, never as a resting state.
    /// </summary>
    public void PulseSignature(OverlayRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (_signature is null || !IsSignature)
        {
            return;
        }

        OverlayAnimations.PlayEventPulse(_signature, SignatureRestingOpacity, diagnostics);
    }

    /// <summary>Short entrance transition, used when the panel becomes visible.</summary>
    public void PlayEntrance(OverlayRenderDiagnostics diagnostics) =>
        OverlayAnimations.PlayEntrance(this, diagnostics);
}
