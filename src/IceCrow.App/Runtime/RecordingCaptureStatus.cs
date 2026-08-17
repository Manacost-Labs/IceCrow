namespace IceCrow.App.Runtime;

/// <summary>State of the match-capture session itself.</summary>
internal enum RecordingSessionPhase
{
    Off,
    WaitingForNextMatch,
    Recording,
}

/// <summary>State of completed-capture persistence, independent of the session.</summary>
internal enum RecordingPersistencePhase
{
    Idle,
    Saving,
    Saved,
    Warning,
    Failed,
}

/// <summary>
/// Immutable developer-facing snapshot of the optional match capture runtime.
/// Session and persistence are orthogonal: a completed match can be saving
/// while the next match is already recording, and neither dimension may
/// overwrite the other. <see cref="LastError"/> reports actual failures,
/// <see cref="LastWarning"/> degraded maintenance after success, and
/// <see cref="LastNotice"/> expected developer actions such as disabling
/// capture mid-match.
/// </summary>
internal sealed record RecordingCaptureStatus(
    bool IsEnabled,
    RecordingSessionPhase SessionPhase,
    int CurrentEventCount,
    long CurrentRetainedBytes,
    RecordingPersistencePhase PersistencePhase,
    int PendingSaveCount,
    int SavedCaptureCount,
    string? LastSavedFileName,
    string? LastNotice,
    string? LastWarning,
    string? LastError);
