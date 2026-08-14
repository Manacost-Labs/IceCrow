using System.Windows;
using System.Windows.Controls;
using IceCrow.Presentation;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// One compact lobby opponent: hero, health, tavern tier, and — in the regular
/// layout only — how old the recorded information is.
/// </summary>
public sealed class OpponentRow : Control
{
    public static readonly DependencyProperty ViewStateProperty =
        DependencyProperty.Register(
            nameof(ViewState),
            typeof(OpponentOverlayViewState),
            typeof(OpponentRow),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(OpponentRow),
            new PropertyMetadata(false));

    static OpponentRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(OpponentRow),
            new FrameworkPropertyMetadata(typeof(OpponentRow)));
    }

    public OpponentOverlayViewState? ViewState
    {
        get => (OpponentOverlayViewState?)GetValue(ViewStateProperty);
        set => SetValue(ViewStateProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}
