namespace IceCrow.App.Runtime;

internal enum RecordingCapturePhase
{
    Off,
    Waiting,
    Recording,
    Saved,
    Failed,
}

/// <summary>Developer-facing snapshot of the optional match capture runtime.</summary>
internal sealed record RecordingCaptureStatus(
    RecordingCapturePhase Phase,
    bool IsEnabled,
    int CurrentEventCount,
    int SavedCaptureCount,
    string? LastSavedFileName,
    string? LastError);
