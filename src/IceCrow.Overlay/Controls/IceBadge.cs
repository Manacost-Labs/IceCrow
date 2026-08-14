using System.Windows;
using System.Windows.Controls;

namespace IceCrow.Overlay.Controls;

/// <summary>Semantic tone of an <see cref="IceBadge"/>.</summary>
public enum IceBadgeTone
{
    Neutral,
    Accent,
    Positive,
    Warning,
    Danger,
}

/// <summary>
/// Small compact marker such as <c>T5</c>, <c>2T</c>, or <c>STALE</c>.
/// The text always carries the meaning; the tone only reinforces it.
/// </summary>
public sealed class IceBadge : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(IceBadge),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(
            nameof(Tone),
            typeof(IceBadgeTone),
            typeof(IceBadge),
            new PropertyMetadata(IceBadgeTone.Neutral));

    static IceBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IceBadge),
            new FrameworkPropertyMetadata(typeof(IceBadge)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IceBadgeTone Tone
    {
        get => (IceBadgeTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}
