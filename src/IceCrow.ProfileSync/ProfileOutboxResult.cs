namespace IceCrow.ProfileSync;

public enum ProfileOutboxResult
{
    Enqueued,

    /// <summary>The event id was already pending; nothing changed.</summary>
    Duplicate,

    /// <summary>A newer latest-only snapshot replaced an older pending one.</summary>
    Replaced,

    /// <summary>
    /// The bounded history is full. The caller must surface this; it is the
    /// only way history is ever not persisted, and it is never silent.
    /// </summary>
    Full,
}
