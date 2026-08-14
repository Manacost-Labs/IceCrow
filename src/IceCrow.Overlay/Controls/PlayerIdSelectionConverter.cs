using System.Globalization;
using System.Windows.Data;

namespace IceCrow.Overlay.Controls;

/// <summary>
/// Compares a lobby row's player id with the currently pinned player id so the
/// selection marker can be data-driven instead of walking item containers.
/// </summary>
public sealed class PlayerIdSelectionConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Length == 2 &&
               values[0] is int playerId &&
               values[1] is int selectedPlayerId &&
               playerId != 0 &&
               playerId == selectedPlayerId;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException("Selection is one-way from the overlay view state.");
}
