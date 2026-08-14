using System.Windows;
using System.Windows.Controls;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// One labelled number. The value leads, the label follows in caption type, so a
/// row of stats reads as data rather than as prose.
/// </summary>
public sealed class IceStat : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(IceStat),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(IceStat),
            new PropertyMetadata(string.Empty));

    static IceStat()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IceStat),
            new FrameworkPropertyMetadata(typeof(IceStat)));
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
