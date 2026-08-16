using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Live;

/// <summary>
/// Receives the exact authoritative event sequence that the live coordinator
/// applies to its tracking session. The coordinator remains the only lifecycle
/// authority: observers see match start first, then every successfully applied
/// normalized event in application order (including buffered pre-start events
/// replayed at match start), and match end at most once per match. Events the
/// tracking session rejects through a safety limit are reported separately and
/// never as applied. Observation is optional infrastructure: an observer that
/// throws is detached permanently so live tracking continues unaffected.
/// </summary>
public interface IAppliedMatchEventObserver
{
    /// <summary>The first notification of every match, exactly once per match.</summary>
    void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId);

    /// <summary>A normalized event was applied to the authoritative session.</summary>
    void OnEventApplied(GameEvent gameEvent);

    /// <summary>
    /// A normalized event was rejected by a tracking safety limit. Diagnostic
    /// only; the event was never applied and must not appear as applied.
    /// </summary>
    void OnEventRejected(GameEvent gameEvent);

    /// <summary>The final notification of a match, at most once per match.</summary>
    void OnMatchEnded(DateTimeOffset timestamp);
}
