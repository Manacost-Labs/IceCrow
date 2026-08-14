using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Tracking;

namespace IceCrow.Live;

public sealed record LiveTrackingUpdate(
    RawLogLine RawLine,
    PowerParseResult ParseResult,
    bool StateChanged,
    TrackingSnapshot? Snapshot,
    LiveTrackingDiagnostics Diagnostics);
