using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace IceCrow.Overlay.Tests;

internal static class VisualTreeSearch
{
    /// <summary>Drains queued layout/loaded work so templates are fully applied.</summary>
    public static void Render(FrameworkElement element)
    {
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        element.UpdateLayout();
    }

    public static List<T> FindChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(DependencyObject parent, List<T> found)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                found.Add(match);
            }

            Collect(child, found);
        }
    }
}
