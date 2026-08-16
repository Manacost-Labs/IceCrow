namespace IceCrow.Hearthstone.Logs;

/// <summary>
/// Why the tailer discarded its checkpoint and restarted the current file from
/// byte zero. Values map one-to-one onto the reset decision points inside
/// <see cref="PowerLogTailer"/> so a full reread is always attributable.
/// </summary>
public enum LogResetReason
{
    /// <summary>The active Power.log path changed to a different file.</summary>
    PathChanged,

    /// <summary>The file creation time changed for the same path.</summary>
    CreationTimeChanged,

    /// <summary>The file became shorter than the consumed byte offset.</summary>
    FileShrank,

    /// <summary>The stored prefix fingerprint no longer matched at open.</summary>
    PrefixChangedBeforeRead,

    /// <summary>Same-length content fingerprint mismatch detected at open.</summary>
    ContentChangedAtEnd,

    /// <summary>The file prefix changed while the tailer was reading.</summary>
    PrefixChangedDuringRead,

    /// <summary>The consumed content window changed while the tailer was reading.</summary>
    ContentChangedDuringRead,
}
