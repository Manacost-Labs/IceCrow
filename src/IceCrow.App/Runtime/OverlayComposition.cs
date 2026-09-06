using System.Windows.Threading;
using IceCrow.Hearthstone.Data;

namespace IceCrow.App.Runtime;

/// <summary>
/// The only place in the runtime that names the overlay presentation type.
/// Keeping the construction inside this method means the overlay assembly is
/// loaded only when the overlay feature is enabled and this method is called.
/// </summary>
internal static class OverlayComposition
{
    public static IOverlayPresentation Create(Dispatcher dispatcher, ICardDatabase cardDatabase) =>
        new PresentationRuntime(dispatcher, cardDatabase);
}
