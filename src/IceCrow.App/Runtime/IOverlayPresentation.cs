using IceCrow.Tracking;

namespace IceCrow.App.Runtime;

/// <summary>
/// The optional WPF overlay seen from the composition root. The runtime only
/// ever talks to this interface so the headless configuration never loads the
/// overlay or presentation assemblies.
/// </summary>
internal interface IOverlayPresentation : IAsyncDisposable
{
    /// <summary>Bounded render counters, or null when diagnostics are not exposed.</summary>
    object? Diagnostics { get; }

    void Start();

    void Publish(TrackingSnapshot snapshot);

    void DisposeSynchronouslyForExit();
}
