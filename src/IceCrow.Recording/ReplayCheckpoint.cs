namespace IceCrow.Recording;

public sealed record ReplayCheckpoint(
    string Name,
    int EventIndex);
